using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using Lumina.Models;
using Lumina.ViewModels;

namespace Lumina.Services;

/// <summary>
/// UI ile donanım arasındaki köprü. UI thread'inde çalışır; tüm donanım işleri
/// <see cref="DdcMonitorService"/> üzerinden arka planda yürür, burada hiçbir şey beklenmez.
/// </summary>
internal sealed class BrightnessController : ObservableObject, IDisposable
{
    private readonly DdcMonitorService _ddc = new();
    private readonly DispatcherTimer _refreshTimer;
    private bool _isRefreshing;
    private bool _refreshQueued;
    private int _initialProbeAttempts;

    public BrightnessController()
    {
        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background);
        _refreshTimer.Tick += async (_, _) =>
        {
            _refreshTimer.Stop();
            await RefreshAsync();
        };

        _ddc.ApplyFailed += _ => Application.Current?.Dispatcher.BeginInvoke(() => ScheduleRefresh(TimeSpan.FromSeconds(2)));
        Monitors.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasMonitors));
    }

    public ObservableCollection<MonitorViewModel> Monitors { get; } = new();

    public bool HasMonitors => Monitors.Count > 0;

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set => SetProperty(ref _isRefreshing, value);
    }

    /// <summary>Ortalama parlaklık değiştiğinde (tepsi ipucu için).</summary>
    public event Action<int>? LevelChanged;

    /// <summary>Kısayol / tepsi tekerleği ile parlaklık değiştirildiğinde (OSD için).</summary>
    public event Action<int>? Adjusted;

    public int AveragePercent => Monitors.Count == 0 ? 0 : (int)Math.Round(Monitors.Average(m => m.Percent));

    public async Task RefreshAsync()
    {
        if (IsRefreshing)
        {
            _refreshQueued = true;
            return;
        }

        IsRefreshing = true;
        try
        {
            var snapshots = await _ddc.RefreshAsync();
            Apply(snapshots);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Lumina] Yenileme hatası: {ex.Message}");
        }
        finally
        {
            IsRefreshing = false;
            if (_refreshQueued)
            {
                _refreshQueued = false;
                ScheduleRefresh(TimeSpan.FromMilliseconds(500));
            }
            else if (Monitors.Count == 0 && _initialProbeAttempts < 3)
            {
                _initialProbeAttempts++;
                ScheduleRefresh(TimeSpan.FromSeconds(2));
            }
        }
    }

    /// <summary>Gecikmeli (debounce) yenileme: ekran değişimi / uyku sonrası gibi olaylar için.</summary>
    public void ScheduleRefresh(TimeSpan delay)
    {
        _refreshTimer.Stop();
        _refreshTimer.Interval = delay;
        _refreshTimer.Start();
    }

    private readonly Dictionary<string, string> _rawNames = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _customNames = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, int> _savedBrightness = new(StringComparer.OrdinalIgnoreCase);
    private Action<string, int>? _onBrightnessPersisted;

    public void SetCustomNames(IReadOnlyDictionary<string, string> customNames)
    {
        _customNames = new Dictionary<string, string>(customNames, StringComparer.OrdinalIgnoreCase);
        foreach (var m in Monitors)
        {
            if (_customNames.TryGetValue(m.Id, out var custom) && !string.IsNullOrWhiteSpace(custom))
                m.Name = custom.Trim();
            else if (_rawNames.TryGetValue(m.Id, out var raw))
                m.Name = raw;
        }
    }

    public void SetSavedBrightness(IReadOnlyDictionary<string, int> savedBrightness, Action<string, int> onBrightnessPersisted)
    {
        _savedBrightness = new Dictionary<string, int>(savedBrightness, StringComparer.OrdinalIgnoreCase);
        _onBrightnessPersisted = onBrightnessPersisted;
    }

    private string ResolveDisplayName(string id, string rawName)
    {
        if (_customNames.TryGetValue(id, out var custom) && !string.IsNullOrWhiteSpace(custom))
            return custom.Trim();

        string? pnp = ExtractPnpCode(id);
        if (pnp is not null)
        {
            var match = _customNames.FirstOrDefault(kv =>
                string.Equals(ExtractPnpCode(kv.Key), pnp, StringComparison.OrdinalIgnoreCase) ||
                kv.Key.Contains(pnp, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match.Value))
                return match.Value.Trim();
        }

        if (_customNames.Count == 1 && !string.IsNullOrWhiteSpace(_customNames.Values.First()))
            return _customNames.Values.First().Trim();

        return rawName;
    }

    private int? ResolveSavedBrightness(string id)
    {
        if (_savedBrightness.TryGetValue(id, out int exact) && exact > 0)
            return exact;

        string? pnp = ExtractPnpCode(id);
        if (pnp is not null)
        {
            var match = _savedBrightness.FirstOrDefault(kv =>
                kv.Value > 0 && (
                    string.Equals(ExtractPnpCode(kv.Key), pnp, StringComparison.OrdinalIgnoreCase) ||
                    kv.Key.Contains(pnp, StringComparison.OrdinalIgnoreCase)
                ));
            if (match.Value > 0)
                return match.Value;
        }

        if (_savedBrightness.Count == 1 && _savedBrightness.Values.First() > 0)
            return _savedBrightness.Values.First();

        return null;
    }

    private static string? ExtractPnpCode(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var parts = id.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && parts[0].Equals("MONITOR", StringComparison.OrdinalIgnoreCase))
            return parts[1];
        return null;
    }

    private void Apply(IReadOnlyList<MonitorSnapshot> snapshots)
    {
        for (int i = Monitors.Count - 1; i >= 0; i--)
        {
            if (!snapshots.Any(s => s.Id == Monitors[i].Id))
                Monitors.RemoveAt(i);
        }

        foreach (var snap in snapshots)
        {
            _rawNames[snap.Id] = snap.Name;
            string displayName = ResolveDisplayName(snap.Id, snap.Name);

            var existing = Monitors.FirstOrDefault(m => m.Id == snap.Id);
            if (existing is null)
            {
                // Yeni monitör eklendi (uygulama açılışı veya yeni takılan monitör):
                int initialPercent;
                int? saved = ResolveSavedBrightness(snap.Id);

                if (saved.HasValue && saved.Value > 0)
                {
                    // Kayıtlı son parlaklık varsa: onu başlangıç değeri yap
                    initialPercent = saved.Value;
                }
                else if (snap.Percent > 0)
                {
                    // Donanım geçerli bir değer bildirdiyse:
                    initialPercent = snap.Percent;
                    _savedBrightness[snap.Id] = snap.Percent;
                    _onBrightnessPersisted?.Invoke(snap.Id, snap.Percent);
                }
                else
                {
                    // Ne kayıt var ne donanım okuyabildi (ör. ilk kurulum + Samsung): varsayılan 50
                    initialPercent = 50;
                }

                var vm = new MonitorViewModel(snap.Id, snap.Name, displayName, initialPercent);
                Monitors.Add(vm);

                // AÇILIŞ SENKRONİZASYONU:
                // Son parlaklığı monitör donanımına doğrudan uygula (fiziksel ekran ile Lumina eşitlenir)!
                vm.LastRequested = initialPercent;
                _ddc.RequestBrightness(snap.Id, initialPercent);
            }
            else
            {
                existing.RawName = snap.Name;
                existing.Name = displayName;

                // Mevcut monitör (ör. Monitörleri Yenile tıklandığında):
                if (snap.Percent > 0)
                {
                    existing.SetFromHardware(snap.Percent);
                    _savedBrightness[snap.Id] = snap.Percent;
                    _onBrightnessPersisted?.Invoke(snap.Id, snap.Percent);
                }
                else if (existing.Percent == 0)
                {
                    int? saved = ResolveSavedBrightness(snap.Id);
                    if (saved.HasValue && saved.Value > 0)
                    {
                        existing.SetFromHardware(saved.Value);
                        _ddc.RequestBrightness(snap.Id, saved.Value);
                    }
                }
                // DİKKAT: snap.Percent == 0 ve existing.Percent > 0 ise:
                // Samsung DDC/CI okuma hatasında donanım hep 0 döndürür; kullanıcının
                // geçerli parlaklığını ASLA 0 ile ezmiyoruz ve mevcut değeri koruyoruz!
            }
        }

        LevelChanged?.Invoke(AveragePercent);
    }

    /// <summary>
    /// Tek bir monitörün parlaklığını ayarlar. Slider ValueChanged'dan her sürükleme adımında çağrılır:
    /// iş yalnızca sözlüğe yazmaktır, donanım çağrısı arka planda yapılır.
    /// </summary>
    public void SetPercent(MonitorViewModel monitor, int percent)
    {
        percent = Math.Clamp(percent, 0, 100);

        if (monitor.Percent != percent)
            monitor.Percent = percent; // slider'ı günceller (yeniden girişte aşağıdaki kontrol yinelemeyi önler)

        if (monitor.LastRequested == percent) return;
        monitor.LastRequested = percent;

        _savedBrightness[monitor.Id] = percent;
        _onBrightnessPersisted?.Invoke(monitor.Id, percent);

        _ddc.RequestBrightness(monitor.Id, percent);
        LevelChanged?.Invoke(AveragePercent);
    }

    /// <summary>Tüm monitörlerde parlaklığı göreli değiştirir (kısayol / tekerlek).</summary>
    public void Adjust(int deltaPercent)
    {
        if (Monitors.Count == 0)
        {
            // Monitör sonradan takılmış / uykudan uyanmış olabilir: yeniden tara.
            ScheduleRefresh(TimeSpan.FromMilliseconds(100));
            return;
        }

        foreach (var monitor in Monitors.ToList())
            SetPercent(monitor, monitor.Percent + deltaPercent);

        Adjusted?.Invoke(AveragePercent);
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
        _ddc.Dispose();
    }
}
