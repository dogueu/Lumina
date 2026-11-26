using Lumina.Models;
using Lumina.Native;

namespace Lumina.Services;

/// <summary>
/// DDC/CI (Dxva2) üzerinden dinamik monitör listeleme ve parlaklık yazma.
/// <list type="bullet">
/// <item>Tüm P/Invoke çağrıları thread-pool'da çalışır; UI thread'i bloklanmaz.</item>
/// <item>I2C yolu tek seferde tek komut kabul ettiği için donanım erişimi <see cref="_bus"/> ile sıralanır.</item>
/// <item>Slider sürüklenirken gelen istekler "son değer kazanır" mantığıyla birleştirilir (coalescing).</item>
/// <item>Her komut try/catch + artan süreli tekrar deneme ile sarılıdır.</item>
/// </list>
/// </summary>
internal sealed class DdcMonitorService : IDisposable
{
    private const int MaxAttempts = 3;
    private const int RetryDelayMs = 40;
    private const int ReadToWriteTurnaroundMs = 40;
    private const int DisposeWaitMs = 500;

    private readonly SemaphoreSlim _bus = new(1, 1);
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _pending = new();

    private List<DdcMonitor> _monitors = new();
    private bool _workerRunning;
    private volatile bool _disposed;

    public event Action<string>? ApplyFailed;

    // ---------------------------------------------------------------- Listeleme

    /// <summary>Bağlı DDC/CI uyumlu monitörleri yeniden listeler ve mevcut parlaklıklarını okur.</summary>
    public Task<IReadOnlyList<MonitorSnapshot>> RefreshAsync(CancellationToken ct = default) => Task.Run(async () =>
    {
        if (_disposed || ct.IsCancellationRequested)
            return (IReadOnlyList<MonitorSnapshot>)Array.Empty<MonitorSnapshot>();

        try
        {
            await _bus.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            return Array.Empty<MonitorSnapshot>();
        }

        try
        {
            if (_disposed || ct.IsCancellationRequested)
                return Array.Empty<MonitorSnapshot>();

            ReleaseHandles();

            var found = new List<DdcMonitor>();
            var snapshots = new List<MonitorSnapshot>();
            var rawMonitors = EnumeratePhysicalMonitors();

            for (int i = 0; i < rawMonitors.Count; i++)
            {
                if (_disposed || ct.IsCancellationRequested)
                {
                    for (int k = i; k < rawMonitors.Count; k++)
                        DestroyMonitorHandle(rawMonitors[k].Handle);
                    foreach (var f in found)
                        DestroyMonitorHandle(f.Handle);
                    return Array.Empty<MonitorSnapshot>();
                }

                var monitor = rawMonitors[i];
                (uint min, uint cur, uint max)? values = null;
                try
                {
                    values = await RetryAsync<(uint min, uint cur, uint max)>(() =>
                        Dxva2.GetMonitorBrightness(monitor.Handle, out var mn, out var c, out var mx)
                            ? (mn, c, mx)
                            : null,
                        ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    for (int k = i; k < rawMonitors.Count; k++)
                        DestroyMonitorHandle(rawMonitors[k].Handle);
                    foreach (var f in found)
                        DestroyMonitorHandle(f.Handle);
                    return Array.Empty<MonitorSnapshot>();
                }
                catch (Exception ex)
                {
                    AppDiagnostics.Trace($"Probe hatası ({monitor.Name}): {ex.Message}");
                }

                if (values is not { } v || v.max <= v.min)
                {
                    // DDC/CI desteklemeyen panellerin handle'ını serbest bırak.
                    DestroyMonitorHandle(monitor.Handle);
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
            if (found.Count > 0 && !ct.IsCancellationRequested && !_disposed)
            {
                // VESA DDC/CI standardı: GetMonitorBrightness okumasından hemen sonra gelen
                // SetMonitorBrightness komutunun I2C kontrolcüsü tarafından yutulmaması için turnaround süresi.
                try
                {
                    await Task.Delay(ReadToWriteTurnaroundMs, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // İptal edildiyse beklemeden dön; handle'lar _monitors içinde korunur/temizlenir.
                }
            }
            return (IReadOnlyList<MonitorSnapshot>)snapshots;
        }
        finally
        {
            try { _bus.Release(); }
            catch (ObjectDisposedException) { }
        }
    }, ct);

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
            PHYSICAL_MONITOR[]? array = null;
            int addedCount = 0;
            try
            {
                if (!Dxva2.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint count) || count == 0)
                    continue;

                array = new PHYSICAL_MONITOR[count];
                if (!Dxva2.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, array))
                {
                    array = null;
                    continue;
                }

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
                    addedCount++;
                }
            }
            catch (Exception ex)
            {
                AppDiagnostics.Trace($"Monitör listeleme hatası: {ex.Message}");
                if (array is not null)
                {
                    for (int k = addedCount; k < array.Length; k++)
                        DestroyMonitorHandle(array[k].hPhysicalMonitor);
                }
            }
        }

        return result;
    }

    private void ReleaseHandles()
    {
        foreach (var m in _monitors)
            DestroyMonitorHandle(m.Handle);
        _monitors = new List<DdcMonitor>();
    }

    private static void DestroyMonitorHandle(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return;
        try
        {
            Dxva2.DestroyPhysicalMonitor(handle);
        }
        catch (Exception ex)
        {
            AppDiagnostics.Trace($"Fiziksel monitör handle kapatılamadı: {ex.Message}");
        }
    }

    // ---------------------------------------------------------------- Yazma

    /// <summary>
    /// Parlaklık isteğini kuyruğa alır ve hemen döner. Aynı monitör için biriken eski istekler atılır;
    /// işçi görev boşta kalınca sonlanır.
    /// </summary>
    public void RequestBrightness(string monitorId, int percent)
    {
        if (_disposed) return;

        lock (_gate)
        {
            if (_disposed) return;
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
                AppDiagnostics.Trace($"Parlaklık yazma hatası: {ex.Message}");
            }
        }
    }

    private async Task ApplyAsync(string id, int percent)
    {
        if (_disposed) return;

        try
        {
            await _bus.WaitAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (_disposed) return;

            var monitor = _monitors.Find(m => m.Id == id);
            if (monitor is null) return;

            uint raw = monitor.ToRaw(percent);
            bool ok = await RetryAsync<bool>(() => Dxva2.SetMonitorBrightness(monitor.Handle, raw) ? true : null)
                .ConfigureAwait(false) == true;

            if (!ok && !_disposed)
            {
                AppDiagnostics.Trace($"SetMonitorBrightness başarısız: {monitor.Name}");
                ApplyFailed?.Invoke(id);
            }
        }
        finally
        {
            try { _bus.Release(); }
            catch (ObjectDisposedException) { }
        }
    }

    /// <summary>
    /// DDC/CI I2C yolu geçici olarak meşgul olabilir: artan beklemelerle tekrar dener.
    /// </summary>
    private static async Task<T?> RetryAsync<T>(Func<T?> operation, CancellationToken ct = default) where T : struct
    {
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var result = operation();
                if (result.HasValue) return result;
            }
            catch (Exception ex)
            {
                AppDiagnostics.Trace($"DDC/CI deneme {attempt} istisna: {ex.Message}");
            }

            if (attempt < MaxAttempts)
                await Task.Delay(RetryDelayMs * attempt, ct).ConfigureAwait(false);
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        bool acquired = false;
        try
        {
            acquired = _bus.Wait(TimeSpan.FromMilliseconds(DisposeWaitMs));
            if (acquired)
                ReleaseHandles();
        }
        catch (ObjectDisposedException)
        {
            // Zaten serbest bırakılmış.
        }
        finally
        {
            if (acquired)
            {
                try { _bus.Release(); }
                catch (ObjectDisposedException) { }
            }
            _bus.Dispose();
        }
    }
}
