using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using Lumina.Native;

namespace Lumina.Services;

/// <summary>
/// Saf Win32 (Shell_NotifyIcon) sistem tepsisi ikonu.
/// <para>
/// Shell, tepsi ikonlarına fare tekerleği mesajı göndermez. Bu yüzden hafif bir WH_MOUSE_LL hook'u
/// kurulur: callback yalnızca WM_MOUSEWHEEL'i inceler, diğer tüm mesajlarda anında geri döner.
/// İkon dikdörtgeni (Shell_NotifyIconGetRect) kısa süreli önbelleğe alınır; böylece başka yerlerde
/// kaydırma yaparken shell'e sürekli sorgu gitmez.
/// </para>
/// </summary>
internal sealed class TrayIconService : IDisposable
{
    private const uint CallbackMessage = User32.WM_APP + 1;
    private const uint IconId = 1;
    private const int WheelNotch = 120;
    private const long RectCacheMs = 400;

    private static readonly string[] TrayWindowClasses =
    {
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "NotifyIconOverflowWindow",
        "TopLevelWindowForOverflowXamlIsland",
    };

    private readonly MessageWindow _window;
    private readonly Dispatcher _dispatcher;
    private readonly uint _taskbarCreatedMessage;
    private readonly LowLevelMouseProc _hookProc; // GC'ye karşı alan olarak tutulur

    private IntPtr _hIcon;
    private IntPtr _hook;
    private bool _added;
    private bool _versionFour;
    private string _tooltip = "Lumina";

    private int _wheelRemainder;
    private RECT _cachedRect;
    private bool _cachedRectValid;
    private long _cachedRectTick;

    public TrayIconService(MessageWindow window)
    {
        _window = window;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _taskbarCreatedMessage = User32.RegisterWindowMessage("TaskbarCreated");
        _hookProc = HookCallback;
        window.AddHook(WndProc);
    }

    /// <summary>Sol tık (flyout aç/kapat).</summary>
    public event Action? LeftClick;

    /// <summary>Sağ tık (bağlam menüsü).</summary>
    public event Action? RightClick;

    /// <summary>Tepsi ikonu üzerinde tekerlek: pozitif = yukarı (parlaklık artır), değer = adım sayısı.</summary>
    public event Action<int>? Wheel;

    public void Show()
    {
        _hIcon = IconFactory.CreateTrayHIcon();
        AddIcon();
        InstallHook();
    }

    public void SetTooltip(string text)
    {
        _tooltip = text;
        if (_added) Modify();
    }

    /// <summary>Tema değişince ikon rengini yeniler.</summary>
    public void RefreshIcon()
    {
        var old = _hIcon;
        _hIcon = IconFactory.CreateTrayHIcon();
        if (_added) Modify();
        if (old != IntPtr.Zero) User32.DestroyIcon(old);
    }

    /// <summary>İkonun ekrandaki (fiziksel piksel) dikdörtgenini döndürür.</summary>
    public bool TryGetIconRect(out RECT rect)
    {
        var id = new NOTIFYICONIDENTIFIER
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONIDENTIFIER>(),
            hWnd = _window.Handle,
            uID = IconId,
        };

        try
        {
            if (Shell32.Shell_NotifyIconGetRect(ref id, out rect) == 0 && rect.Width > 0 && rect.Height > 0)
                return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Lumina] Shell_NotifyIconGetRect hatası: {ex.Message}");
        }

        rect = default;
        return false;
    }

    // ---------------------------------------------------------------- Shell_NotifyIcon

    private NOTIFYICONDATA CreateData(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _window.Handle,
        uID = IconId,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = _hIcon,
        szTip = _tooltip,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private void AddIcon()
    {
        var data = CreateData(Shell32.NIF_MESSAGE | Shell32.NIF_ICON | Shell32.NIF_TIP | Shell32.NIF_SHOWTIP);
        _added = Shell32.Shell_NotifyIcon(Shell32.NIM_ADD, ref data);
        if (!_added)
        {
            Debug.WriteLine("[Lumina] Tepsi ikonu eklenemedi.");
            return;
        }

        data.uTimeoutOrVersion = Shell32.NOTIFYICON_VERSION_4;
        _versionFour = Shell32.Shell_NotifyIcon(Shell32.NIM_SETVERSION, ref data);
    }

    private void Modify()
    {
        var data = CreateData(Shell32.NIF_ICON | Shell32.NIF_TIP | Shell32.NIF_SHOWTIP);
        Shell32.Shell_NotifyIcon(Shell32.NIM_MODIFY, ref data);
    }

    private void RemoveIcon()
    {
        if (!_added) return;
        var data = CreateData(0);
        Shell32.Shell_NotifyIcon(Shell32.NIM_DELETE, ref data);
        _added = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == CallbackMessage)
        {
            handled = true;
            uint evt = _versionFour ? (uint)((long)lParam & 0xFFFF) : (uint)(long)lParam;

            if (_versionFour)
            {
                switch (evt)
                {
                    case Shell32.NIN_SELECT:
                    case Shell32.NIN_KEYSELECT:
                        LeftClick?.Invoke();
                        break;
                    case User32.WM_CONTEXTMENU:
                    case User32.WM_RBUTTONUP:
                        RightClick?.Invoke();
                        break;
                }
            }
            else
            {
                switch (evt)
                {
                    case User32.WM_LBUTTONUP:
                        LeftClick?.Invoke();
                        break;
                    case User32.WM_RBUTTONUP:
                        RightClick?.Invoke();
                        break;
                }
            }
        }
        else if (msg == _taskbarCreatedMessage)
        {
            // Explorer yeniden başladı: ikon kaybolmuştur, yeniden ekle.
            _added = false;
            AddIcon();
        }

        return IntPtr.Zero;
    }

    // ---------------------------------------------------------------- Fare tekerleği

    private void InstallHook()
    {
        if (_hook != IntPtr.Zero) return;
        _hook = User32.SetWindowsHookEx(User32.WH_MOUSE_LL, _hookProc, Kernel32.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            Debug.WriteLine($"[Lumina] Fare hook'u kurulamadı: {Marshal.GetLastWin32Error()}");
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // Sıcak yol: hareket/tıklama mesajlarında hiçbir iş yapmadan dön.
        if (nCode >= 0 && (int)wParam == User32.WM_MOUSEWHEEL)
        {
            try
            {
                int x = Marshal.ReadInt32(lParam, 0);
                int y = Marshal.ReadInt32(lParam, 4);
                int delta = (short)(Marshal.ReadInt32(lParam, 8) >> 16);

                if (IsOverTrayIcon(x, y))
                {
                    // Hook thread'ini (UI) bekletmemek için işi kuyruğa at, olayı yut.
                    _dispatcher.BeginInvoke(DispatcherPriority.Input, () => OnWheelDelta(delta));
                    return (IntPtr)1;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Lumina] Hook hatası: {ex.Message}");
            }
        }

        return User32.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private bool IsOverTrayIcon(int x, int y)
    {
        long now = Environment.TickCount64;
        if (now - _cachedRectTick > RectCacheMs)
        {
            _cachedRectValid = TryGetIconRect(out _cachedRect);
            _cachedRectTick = now;
        }

        if (!_cachedRectValid || !_cachedRect.Contains(x, y)) return false;

        // Gizli (taşma) alanda eski konum önbelleğine karşı: imleç gerçekten bir tepsi penceresinde olmalı.
        return IsTrayWindowAt(x, y);
    }

    private static bool IsTrayWindowAt(int x, int y)
    {
        var hwnd = User32.WindowFromPoint(new POINT { X = x, Y = y });
        if (hwnd == IntPtr.Zero) return false;

        var root = User32.GetAncestor(hwnd, User32.GA_ROOT);
        if (root == IntPtr.Zero) root = hwnd;

        var sb = new StringBuilder(64);
        if (User32.GetClassName(root, sb, sb.Capacity) == 0) return false;

        string cls = sb.ToString();
        foreach (var known in TrayWindowClasses)
        {
            if (string.Equals(cls, known, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    private void OnWheelDelta(int delta)
    {
        // Yüksek çözünürlüklü tekerleklerde (delta < 120) kısmi değerler biriktirilir.
        _wheelRemainder += delta;
        int steps = _wheelRemainder / WheelNotch;
        if (steps == 0) return;

        _wheelRemainder -= steps * WheelNotch;
        Wheel?.Invoke(steps);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            User32.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        RemoveIcon();

        if (_hIcon != IntPtr.Zero)
        {
            User32.DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }
    }
}
