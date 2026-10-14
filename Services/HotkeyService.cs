using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Lumina.Models;
using Lumina.Native;

namespace Lumina.Services;

internal readonly record struct HotkeyRegistrationResult(bool IncreaseRegistered, bool DecreaseRegistered)
{
    public bool Success => IncreaseRegistered && DecreaseRegistered;
}

/// <summary>
/// user32!RegisterHotKey tabanlı global kısayollar. İşletim sistemi tarafından olay güdümlü
/// çalışır (polling / klavye hook'u yok) → boşta sıfır CPU.
/// </summary>
internal sealed class HotkeyService : IDisposable
{
    private const int IncreaseId = 0x4C01;
    private const int DecreaseId = 0x4C02;

    private readonly MessageWindow _window;

    public HotkeyService(MessageWindow window)
    {
        _window = window;
        window.AddHook(WndProc);
    }

    public event Action? IncreaseRequested;
    public event Action? DecreaseRequested;

    /// <summary>Son kayıt hatasının (Win32) açıklaması; çakışma durumunda kullanıcıya gösterilir.</summary>
    public string? LastError { get; private set; }

    public HotkeyRegistrationResult Register(HotkeyGesture increase, HotkeyGesture decrease)
    {
        UnregisterAll();
        LastError = null;

        bool inc = TryRegister(IncreaseId, increase, "Parlaklığı artır");
        bool dec = TryRegister(DecreaseId, decrease, "Parlaklığı azalt");
        return new HotkeyRegistrationResult(inc, dec);
    }

    private bool TryRegister(int id, HotkeyGesture gesture, string label)
    {
        try
        {
            if (User32.RegisterHotKey(_window.Handle, id, gesture.Modifiers, gesture.VirtualKey))
                return true;

            int error = Marshal.GetLastWin32Error();
            LastError = (LastError is null ? "" : LastError + " ")
                        + (error == 1409
                            ? $"“{label}” kısayolu ({gesture}) başka bir uygulama tarafından kullanılıyor."
                            : $"“{label}” kısayolu ({gesture}) kaydedilemedi: {new Win32Exception(error).Message}");
        }
        catch (Exception ex)
        {
            LastError = (LastError is null ? "" : LastError + " ") + ex.Message;
        }

        Debug.WriteLine($"[Lumina] Kısayol kaydı başarısız: {label} ({gesture})");
        return false;
    }

    private void UnregisterAll()
    {
        User32.UnregisterHotKey(_window.Handle, IncreaseId);
        User32.UnregisterHotKey(_window.Handle, DecreaseId);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == User32.WM_HOTKEY)
        {
            switch ((int)wParam)
            {
                case IncreaseId:
                    IncreaseRequested?.Invoke();
                    handled = true;
                    break;
                case DecreaseId:
                    DecreaseRequested?.Invoke();
                    handled = true;
                    break;
            }
        }

        return IntPtr.Zero;
    }

    public void Dispose() => UnregisterAll();
}
