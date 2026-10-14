using Lumina.Native;

namespace Lumina.Models;

/// <summary>appsettings.json içinde kalıcı olarak saklanan kullanıcı ayarları.</summary>
public sealed class AppSettings
{
    public const int MinStep = 1;
    public const int MaxStep = 50;

    /// <summary>Fare tekerleği / kısayol başına parlaklık değişim yüzdesi.</summary>
    public int StepPercent { get; set; } = 5;

    public bool RunAtStartup { get; set; }

    public bool ShowOsd { get; set; } = true;

    public HotkeyGesture IncreaseHotkey { get; set; } = DefaultIncrease;

    public HotkeyGesture DecreaseHotkey { get; set; } = DefaultDecrease;

    /// <summary>Monitör kimliği (ID) bazında kullanıcının belirlediği özel adlar.</summary>
    public Dictionary<string, string> MonitorCustomNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Monitör kimliği (ID) bazında son bilinen parlaklık yüzdesi (Samsung gibi 0 okuyan monitörler ve oturumlar arası hafıza için).</summary>
    public Dictionary<string, int> MonitorBrightness { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ayarlar penceresinin son kullanıcı tarafından belirlenen genişliği.</summary>
    public double SettingsWindowWidth { get; set; } = 880;

    /// <summary>Ayarlar penceresinin son kullanıcı tarafından belirlenen yüksekliği.</summary>
    public double SettingsWindowHeight { get; set; } = 820;

    public static HotkeyGesture DefaultIncrease => new(User32.MOD_CONTROL | User32.MOD_ALT, 0x26); // Ctrl+Alt+↑

    public static HotkeyGesture DefaultDecrease => new(User32.MOD_CONTROL | User32.MOD_ALT, 0x28); // Ctrl+Alt+↓

    public AppSettings Clone() => new()
    {
        StepPercent = StepPercent,
        RunAtStartup = RunAtStartup,
        ShowOsd = ShowOsd,
        IncreaseHotkey = IncreaseHotkey,
        DecreaseHotkey = DecreaseHotkey,
        MonitorCustomNames = new Dictionary<string, string>(MonitorCustomNames, StringComparer.OrdinalIgnoreCase),
        MonitorBrightness = new Dictionary<string, int>(MonitorBrightness, StringComparer.OrdinalIgnoreCase),
        SettingsWindowWidth = SettingsWindowWidth,
        SettingsWindowHeight = SettingsWindowHeight,
    };

    /// <summary>Bozuk / elle düzenlenmiş dosyalara karşı değerleri güvenli aralığa çeker.</summary>
    public void Normalize()
    {
        StepPercent = Math.Clamp(StepPercent, MinStep, MaxStep);
        IncreaseHotkey ??= DefaultIncrease;
        DecreaseHotkey ??= DefaultDecrease;
        MonitorCustomNames ??= new(StringComparer.OrdinalIgnoreCase);
        MonitorBrightness ??= new(StringComparer.OrdinalIgnoreCase);

        if (SettingsWindowWidth < 520 || SettingsWindowWidth > 3840) SettingsWindowWidth = 880;
        if (SettingsWindowHeight < 560 || SettingsWindowHeight > 2160) SettingsWindowHeight = 820;
    }
}
