namespace Lumina.ViewModels;

/// <summary>Flyout'taki tek bir monitör satırı.</summary>
public sealed class MonitorViewModel : ObservableObject
{
    private string _name;
    private int _percent;
    private bool _isHotkeyEnabled;

    public MonitorViewModel(string id, string rawName, string displayName, int percent, bool isHotkeyEnabled = true)
    {
        Id = id;
        RawName = rawName;
        _name = displayName;
        _percent = Math.Clamp(percent, 0, 100);
        _isHotkeyEnabled = isHotkeyEnabled;
        LastRequested = _percent;
    }

    public string Id { get; }

    /// <summary>Donanımın bildirdiği orijinal ad (ör. "Generic PnP Monitor").</summary>
    public string RawName { get; internal set; }

    /// <summary>Kullanıcıya gösterilen görünen ad (varsa özel ad, yoksa RawName).</summary>
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    /// <summary>0-100 arası parlaklık yüzdesi (Slider değerine bağlıdır).</summary>
    public int Percent
    {
        get => _percent;
        set => SetProperty(ref _percent, Math.Clamp(value, 0, 100));
    }

    /// <summary>Monitörün klavye kısayolları ve tepsi tekerleği ile yönetilip yönetilmeyeceği.</summary>
    public bool IsHotkeyEnabled
    {
        get => _isHotkeyEnabled;
        set
        {
            if (SetProperty(ref _isHotkeyEnabled, value))
            {
                OnPropertyChanged(nameof(IsHotkeyDisabled));
                OnPropertyChanged(nameof(HotkeyStatusText));
            }
        }
    }

    public bool IsHotkeyDisabled => !_isHotkeyEnabled;
    public string HotkeyStatusText => _isHotkeyEnabled ? "kısayol aktif" : "kısayol devre dışı";

    /// <summary>Donanıma en son gönderilen değer; aynı değerin tekrar yazılmasını önler.</summary>
    internal int LastRequested { get; set; }

    /// <summary>Donanımdan okunan değeri, geri yazım tetiklemeden uygular.</summary>
    internal void SetFromHardware(int percent)
    {
        LastRequested = Math.Clamp(percent, 0, 100);
        Percent = LastRequested;
    }
}
