using System.Text.Json.Serialization;
using System.Windows.Input;
using Lumina.Native;

namespace Lumina.Models;

/// <summary>Global kısayol: Win32 modifier bayrakları + sanal tuş kodu.</summary>
public sealed record HotkeyGesture(uint Modifiers, uint VirtualKey)
{
    [JsonIgnore] public bool HasCtrl => (Modifiers & User32.MOD_CONTROL) != 0;
    [JsonIgnore] public bool HasAlt => (Modifiers & User32.MOD_ALT) != 0;
    [JsonIgnore] public bool HasShift => (Modifiers & User32.MOD_SHIFT) != 0;
    [JsonIgnore] public bool HasWin => (Modifiers & User32.MOD_WIN) != 0;

    /// <summary>Klavye olayından kısayol üretir. Yalnızca değiştirici (modifier) tuşuna basıldıysa null döner.</summary>
    public static HotkeyGesture? FromKeyEvent(KeyEventArgs e)
    {
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            Key.DeadCharProcessed => e.DeadCharProcessedKey,
            _ => e.Key,
        };

        // Yalnızca modifier tuşlarının kendisine basıldığında tek başına kabul etme (kullanıcı tuş kombinasyonu bekliyor)
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.None or Key.Escape)
        {
            return null;
        }

        uint mods = 0;
        var kbd = Keyboard.Modifiers;
        if (kbd.HasFlag(ModifierKeys.Control)) mods |= User32.MOD_CONTROL;
        if (kbd.HasFlag(ModifierKeys.Alt)) mods |= User32.MOD_ALT;
        if (kbd.HasFlag(ModifierKeys.Shift)) mods |= User32.MOD_SHIFT;
        if (kbd.HasFlag(ModifierKeys.Windows)) mods |= User32.MOD_WIN;

        int vk = KeyInterop.VirtualKeyFromKey(key);
        return vk == 0 ? null : new HotkeyGesture(mods, (uint)vk);
    }

    public override string ToString()
    {
        var parts = new List<string>(5);
        if (HasCtrl) parts.Add("Ctrl");
        if (HasAlt) parts.Add("Alt");
        if (HasShift) parts.Add("Shift");
        if (HasWin) parts.Add("Win");
        parts.Add(KeyName(VirtualKey));
        return string.Join(" + ", parts);
    }

    private static string KeyName(uint vk)
    {
        switch (vk)
        {
            case 0x25: return "←";
            case 0x26: return "↑";
            case 0x27: return "→";
            case 0x28: return "↓";
            case 0x21: return "PageUp";
            case 0x22: return "PageDown";
            case 0x24: return "Home";
            case 0x23: return "End";
            case 0x2D: return "Insert";
            case 0x2E: return "Delete";
            case 0xBB: return "+";
            case 0xBD: return "-";
            case 0x6B: return "Num +";
            case 0x6D: return "Num -";
            case 0x6A: return "Num *";
            case 0x6F: return "Num /";
            case 0x20: return "Space";
        }

        var key = KeyInterop.KeyFromVirtualKey((int)vk);
        if (key >= Key.D0 && key <= Key.D9) return ((int)(key - Key.D0)).ToString();
        if (key >= Key.NumPad0 && key <= Key.NumPad9) return "Num " + (int)(key - Key.NumPad0);
        if (key >= Key.F1 && key <= Key.F24) return key.ToString();
        return key.ToString();
    }
}
