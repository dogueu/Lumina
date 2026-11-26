using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Lumina.Models;
using Lumina.Native;
using Microsoft.Win32;

namespace Lumina.Services;

internal enum AppAppearance { Glass, System, Solid }

/// <summary>
/// 3 görünüm modu (Renkli cam, Sistem/Klasik Windows 11, Katı) x 2 tema (Koyu, Açık) renk token yönetimi.
/// </summary>
internal static class ThemeService
{
    // Sütunlar: Cam Koyu, Cam Açık, Sistem Koyu, Sistem Açık, Katı Koyu, Katı Açık.
    private static readonly Dictionary<string, string[]> Tokens = new()
    {
        ["Window.Tint"] = new[] { "#80000000", "#73FFFFFF", "#FF232323", "#FFF3F3F3", "#FF1C1C1C", "#FFF3F3F3" },
        ["Window.Stroke"] = new[] { "#00000000", "#00000000", "#20FFFFFF", "#1F000000", "#FF2E2E2E", "#FFE0E0E0" },
        ["Card.Fill"] = new[] { "#0FFFFFFF", "#73FFFFFF", "#0DFFFFFF", "#B3FFFFFF", "#FF262626", "#FFFFFFFF" },
        ["Card.Stroke"] = new[] { "#00000000", "#00000000", "#14FFFFFF", "#0F000000", "#00000000", "#FFE8E8E8" },
        ["Card.FillHover"] = new[] { "#1CFFFFFF", "#A6FFFFFF", "#15FFFFFF", "#E6FFFFFF", "#FF2D2D2D", "#FFFAFAFA" },
        ["Tile.Fill"] = new[] { "#14FFFFFF", "#0F000000", "#14FFFFFF", "#0D000000", "#FF2F2F2F", "#FFF0F0F0" },
        ["Text.Primary"] = new[] { "#FFFFFFFF", "#E6000000", "#FFFFFFFF", "#FF1A1A1A", "#FFFFFFFF", "#FF1A1A1A" },
        ["Text.Secondary"] = new[] { "#C7FFFFFF", "#B3000000", "#C7FFFFFF", "#9E000000", "#FFC8C8C8", "#FF4D4D4D" },
        ["Text.Tertiary"] = new[] { "#9EFFFFFF", "#94000000", "#8BFFFFFF", "#8A000000", "#FF9D9D9D", "#FF6B6B6B" },
        ["Divider"] = new[] { "#14FFFFFF", "#12000000", "#18FFFFFF", "#12000000", "#FF2E2E2E", "#FFE8E8E8" },
        ["Level.Track"] = new[] { "#26FFFFFF", "#1F000000", "#2BFFFFFF", "#24000000", "#FF3A3A3A", "#FFDCDCDC" },
        ["Button.Fill"] = new[] { "#0FFFFFFF", "#80FFFFFF", "#0FFFFFFF", "#B3FFFFFF", "#FF2A2A2A", "#FFFFFFFF" },
        ["Segment.Selected"] = new[] { "#26FFFFFF", "#FFFFFFFF", "#1FFFFFFF", "#FFFFFFFF", "#FF3A3A3A", "#FFFFFFFF" },
        ["Slider.ThumbOuter"] = new[] { "#FF454545", "#FFFFFFFF", "#FF0078D4", "#FF0067C0", "#FF454545", "#FFFFFFFF" },
        ["Menu.Fill"] = new[] { "#F2222222", "#F5F9F9F9", "#F2242424", "#F5F9F9F9", "#FF222222", "#FFF9F9F9" },
        ["Menu.Stroke"] = new[] { "#28FFFFFF", "#18000000", "#28FFFFFF", "#18000000", "#FF333333", "#FFE0E0E0" },
    };

    public static AppAppearance Current { get; private set; }
    public static bool IsDark { get; private set; } = true;
    public static bool TransparencyEffects { get; private set; } = true;
    public static bool Blur { get; private set; }
    public static double BlurOpacity => Blur ? 1.0 : 0.0;

    public static event Action? Changed;

    public static AppAppearance Parse(string? value) => value?.ToLowerInvariant() switch
    {
        "system" => AppAppearance.System,
        "solid" => AppAppearance.Solid,
        _ => AppAppearance.Glass,
    };

    public static string ToSetting(AppAppearance a) => a switch
    {
        AppAppearance.System => "system",
        AppAppearance.Solid => "solid",
        _ => "glass",
    };

    public static void Apply(AppAppearance requested, int glassOpacity = 50, bool blur = false)
    {
        IsDark = ReadDword(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 0) != 1;
        TransparencyEffects = ReadDword(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 1) == 1;
        Current = requested;
        Blur = Current == AppAppearance.Glass && blur && TransparencyEffects;

        int column = (int)Current * 2 + (IsDark ? 0 : 1);
        var res = Application.Current.Resources;
        foreach (var (key, values) in Tokens)
            res[key] = Brush(values[column]);

        if (Current == AppAppearance.Glass)
        {
            byte alpha = (byte)Math.Round(Math.Clamp(glassOpacity, AppSettings.MinGlassOpacity, AppSettings.MaxGlassOpacity) * 2.55);
            byte rgb = (byte)(IsDark ? 0 : 255);
            var b = new SolidColorBrush(Color.FromArgb(alpha, rgb, rgb, rgb));
            b.Freeze();
            res["Window.Tint"] = b;
        }

        res["Brand.Dot"] = Brush("#FFFFC21F");
        res["Level.Alert"] = Brush(IsDark ? "#FFFF99A4" : "#FFC42B1C");

        Color track = ((SolidColorBrush)res["Level.Track"]).Color;
        res["Level.TrackFade"] = Gradient((track, 0), (track, 0.35), (WithAlpha(track, 0), 1));

        if (Current == AppAppearance.System)
        {
            var winAccent = WindowsAccentBrush(IsDark);
            Color c = winAccent.Color;
            double lum = 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;

            res["Accent"] = winAccent;
            res["Accent.Foreground"] = Brush(lum > 155 ? "#FF000000" : "#FFFFFFFF");
            res["Accent.Text"] = winAccent;

            res["Ember.Color"] = Colors.Transparent;
            res["Ember.GlowOpacity"] = 0.0;
            res["Ember"] = winAccent;

            res["Slider.Track"] = res["Level.Track"];
            res["Slider.Fill"] = winAccent;
            res["Slider.ThumbOuter"] = winAccent;
            res["Slider.ThumbInner"] = Brush("#FFFFFFFF");

            res["Osd.Tint"] = Brush(IsDark ? "#FF2C2C2E" : "#FFF3F3F3");
            res["Osd.Stroke"] = Brush("#00000000");
            res["Osd.IconBrush"] = res["Text.Primary"];
            res["Osd.Track"] = Brush(IsDark ? "#FF9E9E9E" : "#FFC0C0C0");
        }
        else
        {
            var gold = Brush("#FFFFC21F");
            res["Accent"] = gold;
            res["Accent.Foreground"] = Brush("#FF161616");
            res["Accent.Text"] = !IsDark ? Brush("#FF826313") : gold;

            Color ember = ((SolidColorBrush)res["Accent.Text"]).Color;
            res["Ember.Color"] = ember;
            res["Ember.GlowOpacity"] = 0.55;
            res["Ember"] = Fade(IsDark ? Mix(ember, Colors.White, 0.5) : Mix(ember, Colors.Black, 0.2), ember, 0.22);

            res["Slider.Track"] = res["Level.TrackFade"];
            res["Slider.Fill"] = res["Ember"];
            res["Slider.ThumbInner"] = res["Accent"];

            res["Osd.Tint"] = res["Window.Tint"];
            res["Osd.Stroke"] = res["Window.Stroke"];
            res["Osd.IconBrush"] = res["Accent"];
            res["Osd.Track"] = res["Level.TrackFade"];
        }

        Changed?.Invoke();
    }

    public static void MakeToolWindow(Window window, bool noActivate = false, bool clickThrough = false)
    {
        IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
        long ex = User32.GetWindowLongPtr(hwnd, User32.GWL_EXSTYLE).ToInt64();
        ex = (ex | User32.WS_EX_TOOLWINDOW) & ~User32.WS_EX_APPWINDOW;
        if (noActivate) ex |= User32.WS_EX_NOACTIVATE;
        if (clickThrough) ex |= User32.WS_EX_TRANSPARENT;
        User32.SetWindowLongPtr(hwnd, User32.GWL_EXSTYLE, new IntPtr(ex));

        long style = User32.GetWindowLongPtr(hwnd, User32.GWL_STYLE).ToInt64() & ~User32.WS_SYSMENU;
        User32.SetWindowLongPtr(hwnd, User32.GWL_STYLE, new IntPtr(style));
    }

    private static SolidColorBrush WindowsAccentBrush(bool dark)
    {
        try
        {
            var c = new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
            if (c.A > 0)
            {
                var b = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
                b.Freeze();
                return b;
            }
        }
        catch (Exception ex) { AppDiagnostics.Trace($"UISettings vurgu rengi okunamadı: {ex.Message}"); }

        try
        {
            int abgr = ReadDword(@"Software\Microsoft\Windows\DWM", "AccentColor", 0);
            if (abgr != 0)
            {
                var b = new SolidColorBrush(Color.FromRgb((byte)(abgr & 0xFF), (byte)((abgr >> 8) & 0xFF), (byte)((abgr >> 16) & 0xFF)));
                b.Freeze();
                return b;
            }

            var g = SystemParameters.WindowGlassColor;
            if (g.A > 0)
            {
                var b = new SolidColorBrush(Color.FromRgb(g.R, g.G, g.B));
                b.Freeze();
                return b;
            }
        }
        catch (Exception ex) { AppDiagnostics.Trace($"DWM vurgu rengi okunamadı: {ex.Message}"); }

        return Brush(dark ? "#FF0078D4" : "#FF0067C0");
    }

    private static Brush Fade(Color start, Color c, double endAlpha) =>
        Gradient((start, 0), (c, 0.3), (WithAlpha(c, endAlpha), 1));

    private static Brush Gradient(params (Color Color, double At)[] stops)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        foreach (var (color, at) in stops) b.GradientStops.Add(new GradientStop(color, at));
        b.Freeze();
        return b;
    }

    private static Color WithAlpha(Color c, double a) =>
        Color.FromArgb((byte)Math.Round(c.A * a), c.R, c.G, c.B);

    private static Color Mix(Color a, Color b, double t) => Color.FromArgb(a.A,
        (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t),
        (byte)Math.Round(a.B + (b.B - a.B) * t));

    private static int ReadDword(string path, string name, int fallback)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(path);
            return key?.GetValue(name) is int v ? v : fallback;
        }
        catch (Exception ex)
        {
            AppDiagnostics.Trace($"Registry DWORD okunamadı ({path}\\{name}): {ex.Message}");
            return fallback;
        }
    }

    private static SolidColorBrush Brush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}
