using Lumina.Native;

namespace Lumina.Models;

/// <summary>appsettings.json içinde kalıcı olarak saklanan kullanıcı ayarları.</summary>
public sealed class AppSettings
{
    public const int MinStep = 1;
    public const int MaxStep = 50;
    public const int MinGlassOpacity = 20;
    public const int MaxGlassOpacity = 95;
    public const int DefaultGlassOpacity = 50;

    /// <summary>Fare tekerleği / kısayol başına parlaklık değişim yüzdesi.</summary>
    public int StepPercent { get; set; } = 5;

    public bool RunAtStartup { get; set; }

    public bool ShowOsd { get; set; } = true;

    public HotkeyGesture IncreaseHotkey { get; set; } = DefaultIncrease;

    public HotkeyGesture DecreaseHotkey { get; set; } = DefaultDecrease;

    /// <summary>Monitör kimliği (ID) bazında kullanıcının belirlediği özel adlar.</summary>
    public Dictionary<string, string> MonitorCustomNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Monitör kimliği (ID) bazında son bilinen parlaklık yüzdesi (donanımdan 0 okuyan monitörler ve oturumlar arası hafıza için).</summary>
    public Dictionary<string, int> MonitorBrightness { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Klavye kısayolları ve tepsi tekerleğinden hariç tutulan monitör kimlikleri (ID).</summary>
    public HashSet<string> ExcludedHotkeyMonitors { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Kullanıcının belirlediği monitör görüntüleme sırası (kimlik ID listesi).</summary>
    public List<string> MonitorOrder { get; set; } = new();

    /// <summary>Panel görünüm modu ("glass", "system", "solid").</summary>
    public string Appearance { get; set; } = "glass";

    /// <summary>Renkli cam arka plan film opaklığı (%20 - %95).</summary>
    public int GlassOpacity { get; set; } = DefaultGlassOpacity;

    /// <summary>Renkli cam modunda arka plan bulanıklaştırması açık mı.</summary>
    public bool GlassBlur { get; set; }

    public static HotkeyGesture DefaultIncrease => new(User32.MOD_CONTROL | User32.MOD_ALT, 0x26); // Ctrl+Alt+↑

    public static HotkeyGesture DefaultDecrease => new(User32.MOD_CONTROL | User32.MOD_ALT, 0x28); // Ctrl+Alt+↓

    /// <summary>Bozuk / elle düzenlenmiş dosyalara karşı değerleri güvenli aralığa çeker.</summary>
    public void Normalize()
    {
        StepPercent = Math.Clamp(StepPercent, MinStep, MaxStep);
        IncreaseHotkey ??= DefaultIncrease;
        DecreaseHotkey ??= DefaultDecrease;
        MonitorCustomNames = MonitorCustomNames is null
            ? new(StringComparer.OrdinalIgnoreCase)
            : new(MonitorCustomNames, StringComparer.OrdinalIgnoreCase);
        MonitorBrightness = MonitorBrightness is null
            ? new(StringComparer.OrdinalIgnoreCase)
            : new(MonitorBrightness, StringComparer.OrdinalIgnoreCase);
        ExcludedHotkeyMonitors = ExcludedHotkeyMonitors is null
            ? new(StringComparer.OrdinalIgnoreCase)
            : new(ExcludedHotkeyMonitors.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);
        MonitorOrder = MonitorOrder is null
            ? new()
            : MonitorOrder
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        Appearance = Appearance?.ToLowerInvariant() switch
        {
            "system" => "system",
            "solid" => "solid",
            _ => "glass",
        };
        GlassOpacity = Math.Clamp(GlassOpacity == 0 ? DefaultGlassOpacity : GlassOpacity, MinGlassOpacity, MaxGlassOpacity);
    }
}
