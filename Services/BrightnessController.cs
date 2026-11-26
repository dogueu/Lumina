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
    private const int ConfirmDelayMs = 1200;
    private const int QueuedRefreshDelayMs = 300;
    private const int RetryProbeDelayMs = 500;
    private const int ApplyFailureRefreshDelayMs = 800;
    private const int FallbackBrightnessPercent = 50;
    private const int MaxInitialProbeAttempts = 6;

    private readonly DdcMonitorService _ddc = new();
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _confirmTimer;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly Dictionary<string, string> _rawNames = new(StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, string> _customNames = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, int> _savedBrightness = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _excludedHotkeyMonitors = new(StringComparer.OrdinalIgnoreCase);
    private List<string> _monitorOrder = new();
    private Action<string, int>? _onBrightnessPersisted;

    private bool _isRefreshing;
    private bool _refreshQueued;
    private int _initialProbeAttempts;
    private bool _disposed;

    public BrightnessController()
    {
        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background);
        _refreshTimer.Tick += OnRefreshTimerTick;

        _confirmTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(ConfirmDelayMs)
        };
        _confirmTimer.Tick += (_, _) =>
        {
            _confirmTimer.Stop();
            if (_disposed) return;
            foreach (var m in Monitors)
                _ddc.RequestBrightness(m.Id, m.Percent);
        };

        _ddc.ApplyFailed += OnDdcApplyFailed;
        Monitors.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasMonitors));
    }

    private void OnDdcApplyFailed(string _)
    {
        if (_disposed) return;
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (!_disposed)
                ScheduleRefresh(TimeSpan.FromMilliseconds(ApplyFailureRefreshDelayMs));
        });
    }

    private async void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        if (_disposed) return;

        try
        {
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            AppDiagnostics.Trace($"Zamanlanmış yenileme hatası: {ex.Message}");
        }
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

    /// <summary>En az bir monitörün klavye kısayolu ve tepsi tekerleğine dahil olup olmadığı.</summary>
    public bool HasActiveHotkeyMonitors => Monitors.Any(m => m.IsHotkeyEnabled);

    /// <summary>Kısayol ve tepsi tekerleğine dahil (aktif) monitörlerin parlaklık ortalaması.</summary>
    public int ActiveAveragePercent
    {
        get
        {
            int sum = 0, count = 0;
            foreach (var m in Monitors)
            {
                if (!m.IsHotkeyEnabled) continue;
                sum += m.Percent;
                count++;
            }
            return count == 0 ? 0 : (int)Math.Round((double)sum / count);
        }
    }

    public async Task RefreshAsync()
    {
        if (_disposed) return;

        if (IsRefreshing)
        {
            _refreshQueued = true;
            return;
        }

        IsRefreshing = true;
        try
        {
            var snapshots = await _ddc.RefreshAsync(_disposeCts.Token);
            if (_disposed) return;
            Apply(snapshots);
        }
        catch (OperationCanceledException)
        {
            // Kapanış sırasında iptal edilir.
        }
        catch (Exception ex)
        {
            AppDiagnostics.Trace($"Yenileme hatası: {ex.Message}");
        }
        finally
        {
            IsRefreshing = false;
            if (!_disposed)
            {
                if (_refreshQueued)
                {
                    _refreshQueued = false;
                    ScheduleRefresh(TimeSpan.FromMilliseconds(QueuedRefreshDelayMs));
                }
                else if (Monitors.Count == 0 && _initialProbeAttempts < MaxInitialProbeAttempts)
                {
                    _initialProbeAttempts++;
                    ScheduleRefresh(TimeSpan.FromMilliseconds(RetryProbeDelayMs));
                }
            }
        }
    }

    /// <summary>Gecikmeli (debounce) yenileme: ekran değişimi / uyku sonrası gibi olaylar için.</summary>
    public void ScheduleRefresh(TimeSpan delay)
    {
        if (_disposed) return;
        _refreshTimer.Stop();
        _refreshTimer.Interval = delay;
        _refreshTimer.Start();
    }

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

    public void SetExcludedHotkeyMonitors(IReadOnlyCollection<string> excludedIds)
    {
        _excludedHotkeyMonitors = new HashSet<string>(excludedIds, StringComparer.OrdinalIgnoreCase);
        var allIds = Monitors.Select(m => m.Id).ToList();
        foreach (var m in Monitors)
            m.IsHotkeyEnabled = !IsMonitorExcluded(m.Id, _excludedHotkeyMonitors, allIds);

        LevelChanged?.Invoke(ActiveAveragePercent);
    }

    public void SetMonitorOrder(IReadOnlyList<string> order)
    {
        _monitorOrder = order is null
            ? new()
            : order.Where(id => !string.IsNullOrWhiteSpace(id))
                   .Distinct(StringComparer.OrdinalIgnoreCase)
                   .ToList();
        ApplyMonitorOrder();
    }

    public void MoveMonitor(int oldIndex, int newIndex)
    {
        if (oldIndex == newIndex
            || oldIndex < 0 || oldIndex >= Monitors.Count
            || newIndex < 0 || newIndex >= Monitors.Count)
            return;

        Monitors.Move(oldIndex, newIndex);
        _monitorOrder = Monitors.Select(m => m.Id).ToList();
    }

    private int ResolveOrderIndex(string monitorId, IReadOnlyList<string> allMonitorIds)
    {
        for (int i = 0; i < _monitorOrder.Count; i++)
        {
            if (string.Equals(_monitorOrder[i], monitorId, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        if (HasUniquePnp(monitorId, allMonitorIds, out string pnp))
        {
            for (int i = 0; i < _monitorOrder.Count; i++)
            {
                if (string.Equals(ExtractPnpCode(_monitorOrder[i]), pnp, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }

        return int.MaxValue;
    }

    private void ApplyMonitorOrder()
    {
        if (_monitorOrder.Count == 0 || Monitors.Count <= 1) return;

        var allIds = Monitors.Select(m => m.Id).ToList();
        var sorted = Monitors
            .Select((m, idx) => (Monitor: m, Order: ResolveOrderIndex(m.Id, allIds), OriginalIndex: idx))
            .OrderBy(x => x.Order)
            .ThenBy(x => x.OriginalIndex)
            .Select(x => x.Monitor)
            .ToList();

        for (int targetIdx = 0; targetIdx < sorted.Count; targetIdx++)
        {
            int curIdx = Monitors.IndexOf(sorted[targetIdx]);
            if (curIdx >= 0 && curIdx != targetIdx)
                Monitors.Move(curIdx, targetIdx);
        }
    }

    public void SetSavedBrightness(IReadOnlyDictionary<string, int> savedBrightness, Action<string, int> onBrightnessPersisted)
    {
        _savedBrightness = new Dictionary<string, int>(savedBrightness, StringComparer.OrdinalIgnoreCase);
        _onBrightnessPersisted = onBrightnessPersisted;
    }

    private string ResolveDisplayName(string id, string rawName, IReadOnlyList<string> allMonitorIds)
    {
        if (_customNames.TryGetValue(id, out var custom) && !string.IsNullOrWhiteSpace(custom))
            return custom.Trim();

        if (HasUniquePnp(id, allMonitorIds, out string pnp))
        {
            var match = _customNames.FirstOrDefault(kv =>
                string.Equals(ExtractPnpCode(kv.Key), pnp, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match.Value))
                return match.Value.Trim();
        }

        if (allMonitorIds.Count == 1 && _customNames.Count == 1 && !string.IsNullOrWhiteSpace(_customNames.Values.First()))
            return _customNames.Values.First().Trim();

        return rawName;
    }

    private int? ResolveSavedBrightness(string id, IReadOnlyList<string> allMonitorIds)
    {
        if (_savedBrightness.TryGetValue(id, out int exact) && exact >= 0)
            return Math.Clamp(exact, 0, 100);

        if (HasUniquePnp(id, allMonitorIds, out string pnp))
        {
            foreach (var kv in _savedBrightness)
            {
                if (kv.Value >= 0 && string.Equals(ExtractPnpCode(kv.Key), pnp, StringComparison.OrdinalIgnoreCase))
                    return Math.Clamp(kv.Value, 0, 100);
            }
        }

        if (allMonitorIds.Count == 1 && _savedBrightness.Count == 1)
        {
            int only = _savedBrightness.Values.First();
            if (only >= 0)
                return Math.Clamp(only, 0, 100);
        }

        return null;
    }

    internal static string? ExtractPnpCode(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var parts = id.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && parts[0].Equals("MONITOR", StringComparison.OrdinalIgnoreCase))
            return parts[1];
        return null;
    }

    internal static bool HasUniquePnp(string monitorId, IEnumerable<string> allMonitorIds, out string pnp)
    {
        pnp = ExtractPnpCode(monitorId) ?? string.Empty;
        if (pnp.Length == 0) return false;

        int count = 0;
        foreach (var id in allMonitorIds)
        {
            if (string.Equals(ExtractPnpCode(id), pnp, StringComparison.OrdinalIgnoreCase))
                count++;
        }
        return count == 1;
    }

    internal static bool IsMonitorExcluded(string monitorId, IReadOnlySet<string> excludedIds, IEnumerable<string> allMonitorIds)
    {
        if (excludedIds.Count == 0) return false;
        if (excludedIds.Contains(monitorId)) return true;

        if (!HasUniquePnp(monitorId, allMonitorIds, out string pnp)) return false;

        foreach (var ex in excludedIds)
        {
            if (string.Equals(ExtractPnpCode(ex), pnp, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private void Apply(IReadOnlyList<MonitorSnapshot> snapshots)
    {
        for (int i = Monitors.Count - 1; i >= 0; i--)
        {
            if (!snapshots.Any(s => s.Id == Monitors[i].Id))
                Monitors.RemoveAt(i);
        }

        var snapshotIds = snapshots.Select(s => s.Id).ToList();
        bool anyAdded = false;

        foreach (var snap in snapshots)
        {
            _rawNames[snap.Id] = snap.Name;
            string displayName = ResolveDisplayName(snap.Id, snap.Name, snapshotIds);
            bool hotkeyEnabled = !IsMonitorExcluded(snap.Id, _excludedHotkeyMonitors, snapshotIds);

            var existing = Monitors.FirstOrDefault(m => m.Id == snap.Id);
            if (existing is null)
            {
                int initialPercent;
                int? saved = ResolveSavedBrightness(snap.Id, snapshotIds);

                if (saved.HasValue)
                {
                    initialPercent = saved.Value;
                    if (!_savedBrightness.TryGetValue(snap.Id, out int curSaved) || curSaved != initialPercent)
                    {
                        _savedBrightness[snap.Id] = initialPercent;
                        _onBrightnessPersisted?.Invoke(snap.Id, initialPercent);
                    }
                }
                else if (snap.Percent > 0)
                {
                    initialPercent = snap.Percent;
                    _savedBrightness[snap.Id] = initialPercent;
                    _onBrightnessPersisted?.Invoke(snap.Id, initialPercent);
                }
                else
                {
                    // Kayıtlı değer yoksa ve donanım geçerli bir okuma döndürmediyse güvenli başlangıç değeri ata.
                    initialPercent = FallbackBrightnessPercent;
                    _savedBrightness[snap.Id] = initialPercent;
                    _onBrightnessPersisted?.Invoke(snap.Id, initialPercent);
                }

                var vm = new MonitorViewModel(snap.Id, snap.Name, displayName, initialPercent, hotkeyEnabled);
                Monitors.Add(vm);

                vm.LastRequested = initialPercent;
                _ddc.RequestBrightness(snap.Id, initialPercent);
                anyAdded = true;
            }
            else
            {
                existing.RawName = snap.Name;
                existing.Name = displayName;
                existing.IsHotkeyEnabled = hotkeyEnabled;

                int? saved = ResolveSavedBrightness(snap.Id, snapshotIds);
                if (saved.HasValue)
                {
                    existing.SetFromHardware(saved.Value);
                    _ddc.RequestBrightness(snap.Id, saved.Value);
                }
                else if (snap.Percent > 0)
                {
                    existing.SetFromHardware(snap.Percent);
                    _savedBrightness[snap.Id] = snap.Percent;
                    _onBrightnessPersisted?.Invoke(snap.Id, snap.Percent);
                }
            }
        }

        if (anyAdded)
        {
            _confirmTimer.Stop();
            _confirmTimer.Start();
        }

        ApplyMonitorOrder();
        LevelChanged?.Invoke(ActiveAveragePercent);
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

        if (!_savedBrightness.TryGetValue(monitor.Id, out int prevSaved) || prevSaved != percent)
        {
            _savedBrightness[monitor.Id] = percent;
            _onBrightnessPersisted?.Invoke(monitor.Id, percent);
        }

        if (monitor.LastRequested == percent) return;
        monitor.LastRequested = percent;

        _ddc.RequestBrightness(monitor.Id, percent);
        LevelChanged?.Invoke(ActiveAveragePercent);
    }

    /// <summary>Kısayol ve tepsi tekerleğine dahil olan monitörlerde parlaklığı göreli değiştirir.</summary>
    public void Adjust(int deltaPercent)
    {
        if (Monitors.Count == 0)
        {
            // Monitör sonradan takılmış / uykudan uyanmış olabilir: yeniden tara.
            ScheduleRefresh(TimeSpan.FromMilliseconds(100));
            return;
        }

        var targets = Monitors.Where(m => m.IsHotkeyEnabled).ToList();
        if (targets.Count == 0) return;

        foreach (var monitor in targets)
            SetPercent(monitor, monitor.Percent + deltaPercent);

        Adjusted?.Invoke(ActiveAveragePercent);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _refreshTimer.Stop();
        _confirmTimer.Stop();
        _ddc.ApplyFailed -= OnDdcApplyFailed;
        _disposeCts.Cancel();
        _ddc.Dispose();
        _disposeCts.Dispose();
    }
}
