using System.Diagnostics;
using Lumina.Models;
using Lumina.Native;

namespace Lumina.Services;

/// <summary>
/// DDC/CI (Dxva2) üzerinden dinamik monitör listeleme ve parlaklık yazma.
/// <list type="bullet">
/// <item>Tüm P/Invoke çağrıları thread-pool'da çalışır; UI thread'i asla bloklanmaz.</item>
/// <item>I2C yolu tek seferde tek komut kabul ettiği için donanım erişimi <see cref="_bus"/> ile sıralanır.</item>
/// <item>Slider sürüklenirken gelen istekler "son değer kazanır" mantığıyla birleştirilir (coalescing);
/// böylece kuyruk birikmez ve parlaklık gecikmeden son değere ulaşır.</item>
/// <item>Her komut try/catch + kısa süreli retry (artan bekleme) ile sarılıdır.</item>
/// </list>
/// </summary>
internal sealed class DdcMonitorService : IDisposable
{
    private const int MaxAttempts = 3;
    private const int RetryDelayMs = 40;

    private readonly SemaphoreSlim _bus = new(1, 1);
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _pending = new();

    private List<DdcMonitor> _monitors = new();   // yalnızca _bus altında okunur/yazılır
    private bool _workerRunning;
    private volatile bool _disposed;

    /// <summary>Bir monitöre yazma tüm denemelere rağmen başarısız olursa tetiklenir (thread-pool'dan).</summary>
    public event Action<string>? ApplyFailed;

    // ---------------------------------------------------------------- Listeleme

    /// <summary>Bağlı DDC/CI uyumlu monitörleri yeniden listeler ve mevcut parlaklıklarını okur.</summary>
    public Task<IReadOnlyList<MonitorSnapshot>> RefreshAsync() => Task.Run(async () =>
    {
        await _bus.WaitAsync().ConfigureAwait(false);
        try
        {
            ReleaseHandles();

            var found = new List<DdcMonitor>();
            var snapshots = new List<MonitorSnapshot>();

            foreach (var monitor in EnumeratePhysicalMonitors())
            {
                (uint min, uint cur, uint max)? values = null;
                try
                {
                    values = await RetryAsync<(uint min, uint cur, uint max)>(() =>
                        Dxva2.GetMonitorBrightness(monitor.Handle, out var mn, out var c, out var mx)
                            ? (mn, c, mx)
                            : null).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Lumina] Probe hatası ({monitor.Name}): {ex.Message}");
                }

                if (values is not { } v || v.max <= v.min)
                {
                    // DDC/CI desteklemiyor (ör. dizüstü iç paneli) – handle'ı bırak.
                    Dxva2.DestroyPhysicalMonitor(monitor.Handle);
                    continue;
                }

                var ddc = new DdcMonitor
                {
                    Id = monitor.Id,
                    Name = monitor.Name,
                    Handle = monitor.Handle,
                    Min = v.min,
                    Max = v.max,
                };
                found.Add(ddc);
                snapshots.Add(new MonitorSnapshot(ddc.Id, ddc.Name, ddc.ToPercent(v.cur)));
            }

            _monitors = found;
            return (IReadOnlyList<MonitorSnapshot>)snapshots;
        }
        finally
        {
            _bus.Release();
        }
    });

    private readonly record struct RawMonitor(string Id, string Name, IntPtr Handle);

    private static List<RawMonitor> EnumeratePhysicalMonitors()
    {
        var result = new List<RawMonitor>();
        var hMonitors = new List<IntPtr>();

        MonitorEnumProc callback = (IntPtr hMonitor, IntPtr _, ref RECT _, IntPtr _) =>
        {
            hMonitors.Add(hMonitor);
            return true;
        };

        try
        {
            User32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        }
        finally
        {
            GC.KeepAlive(callback);
        }

        int displayIndex = 0;
        foreach (var hMonitor in hMonitors)
        {
            displayIndex++;
            try
            {
                if (!Dxva2.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint count) || count == 0)
                    continue;

                var array = new PHYSICAL_MONITOR[count];
                if (!Dxva2.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, array))
                    continue;

                var info = new MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFOEX>() };
                string device = User32.GetMonitorInfo(hMonitor, ref info) ? info.szDevice : $"DISPLAY{displayIndex}";

                for (int i = 0; i < array.Length; i++)
                {
                    var dd = new DISPLAY_DEVICE { cb = System.Runtime.InteropServices.Marshal.SizeOf<DISPLAY_DEVICE>() };
                    bool hasDd = User32.EnumDisplayDevices(device, (uint)i, ref dd, 0);

                    string name = hasDd && !string.IsNullOrWhiteSpace(dd.DeviceString)
                        ? dd.DeviceString
                        : array[i].szPhysicalMonitorDescription;
                    if (string.IsNullOrWhiteSpace(name)) name = $"Monitör {displayIndex}";

                    string id = hasDd && !string.IsNullOrWhiteSpace(dd.DeviceID)
                        ? dd.DeviceID
                        : $"{device}#{i}";

                    result.Add(new RawMonitor(id, name, array[i].hPhysicalMonitor));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Lumina] Monitör listeleme hatası: {ex.Message}");
            }
        }

        return result;
    }

    private void ReleaseHandles()
    {
        foreach (var m in _monitors)
        {
            try { Dxva2.DestroyPhysicalMonitor(m.Handle); } catch { /* yoksay */ }
        }
        _monitors = new List<DdcMonitor>();
    }

    // ---------------------------------------------------------------- Yazma

    /// <summary>
    /// Parlaklık isteğini kuyruğa alır ve hemen döner. Aynı monitör için biriken eski istekler atılır;
    /// işçi görev boşta kalınca sonlanır (arka planda thread/bellek tutulmaz).
    /// </summary>
    public void RequestBrightness(string monitorId, int percent)
    {
        if (_disposed) return;

        lock (_gate)
        {
            _pending[monitorId] = Math.Clamp(percent, 0, 100);
            if (_workerRunning) return;
            _workerRunning = true;
        }

        _ = Task.Run(ProcessPendingAsync);
    }

    private async Task ProcessPendingAsync()
    {
        while (true)
        {
            string id;
            int percent;

            lock (_gate)
            {
                if (_pending.Count == 0 || _disposed)
                {
                    _workerRunning = false;
                    return;
                }

                var first = _pending.First();
                id = first.Key;
                percent = first.Value;
                _pending.Remove(id);
            }

            try
            {
                await ApplyAsync(id, percent).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Lumina] Parlaklık yazma hatası: {ex.Message}");
            }
        }
    }

    private async Task ApplyAsync(string id, int percent)
    {
        await _bus.WaitAsync().ConfigureAwait(false);
        try
        {
            var monitor = _monitors.Find(m => m.Id == id);
            if (monitor is null) return;

            uint raw = monitor.ToRaw(percent);
            bool ok = await RetryAsync<bool>(() => Dxva2.SetMonitorBrightness(monitor.Handle, raw) ? true : null)
                .ConfigureAwait(false) == true;

            if (!ok)
            {
                Debug.WriteLine($"[Lumina] SetMonitorBrightness başarısız: {monitor.Name}");
                ApplyFailed?.Invoke(id);
            }
        }
        finally
        {
            _bus.Release();
        }
    }

    /// <summary>
    /// DDC/CI I2C yolu geçici olarak meşgul/gecikmeli olabilir: kısa artan beklemelerle tekrar dener.
    /// İşlem null / false-benzeri dönerse veya istisna atarsa başarısız sayılır.
    /// </summary>
    private static async Task<T?> RetryAsync<T>(Func<T?> operation) where T : struct
    {
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                var result = operation();
                if (result.HasValue) return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Lumina] DDC/CI deneme {attempt} istisna: {ex.Message}");
            }

            if (attempt < MaxAttempts)
                await Task.Delay(RetryDelayMs * attempt).ConfigureAwait(false);
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Donanım erişimi bitene kadar (kısa) bekle, ardından handle'ları kapat.
        if (_bus.Wait(TimeSpan.FromSeconds(2)))
        {
            try { ReleaseHandles(); }
            finally { _bus.Release(); }
        }
    }
}
