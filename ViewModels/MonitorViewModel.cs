namespace Lumina.ViewModels;

/// <summary>Flyout'taki tek bir monitör satırı.</summary>
public sealed class MonitorViewModel : ObservableObject
{
    private string _name;
    private int _percent;

    public MonitorViewModel(string id, string rawName, string displayName, int percent)
    {
        Id = id;
        RawName = rawName;
        _name = displayName;
        _percent = Math.Clamp(percent, 0, 100);
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

    /// <summary>0-100 arası parlaklık yüzdesi (Slider ile iki yönlü bağlıdır).</summary>
    public int Percent
    {
        get => _percent;
        set => SetProperty(ref _percent, Math.Clamp(value, 0, 100));
    }

    /// <summary>Donanıma en son gönderilen değer; aynı değerin tekrar yazılmasını önler.</summary>
    internal int LastRequested { get; set; }

    /// <summary>Donanımdan okunan değeri, geri yazım tetiklemeden uygular.</summary>
    internal void SetFromHardware(int percent)
    {
        LastRequested = Math.Clamp(percent, 0, 100);
        Percent = LastRequested;
    }
}
