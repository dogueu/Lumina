using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using Lumina.Native;
using Lumina.Services;
using Lumina.ViewModels;
using Wpf.Ui.Appearance;

namespace Lumina;

/// <summary>
/// Sistem tepsisi flyout'u (Windows 11 Hızlı Ayarlar benzeri):
/// Görev çubuğunun hemen üzerinde sağ alt köşede açılır; odak kaybında veya dışarı tıklandığında kapanır.
/// </summary>
public partial class MainWindow : Window
{
    private const double SlideDistanceDip = 8;
    private const double GapDip = 12; // Windows 11 flyout'larının görev çubuğuna boşluğu

    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction EaseIn = new CubicEase { EasingMode = EasingMode.EaseIn };

    private readonly BrightnessController _controller;
    private readonly Action _openSettings;
    private readonly Func<int> _getStepPercent;

    private bool _hiding;
    private int _slideDirection = 1; // +1: aşağıdan yukarı, -1: yukarıdan aşağı
    private long _lastAutoHideTick;
    private long _showTick;

    private IntPtr _mouseHook = IntPtr.Zero;
    private LowLevelMouseProc? _mouseHookProc;

    internal MainWindow(BrightnessController controller, Action openSettings, Func<int> getStepPercent)
    {
        InitializeComponent();

        _controller = controller;
        _openSettings = openSettings;
        _getStepPercent = getStepPercent;
        DataContext = controller;

        Background = Brushes.Transparent;
        Icon = IconFactory.RenderSun(64, Colors.White);

        controller.PropertyChanged += (_, _) => UpdateEmptyState();
        UpdateEmptyState();

        Deactivated += OnDeactivated;
        Closed += (_, _) => StopMouseHook();
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            int doNotRound = DwmApi.DWMWCP_DONOTROUND;
            DwmApi.DwmSetWindowAttribute(handle, DwmApi.DWMWA_WINDOW_CORNER_PREFERENCE, ref doNotRound, sizeof(int));
        };

        ApplyFlyoutTheme();
    }

    public bool IsOpen => IsVisible && !_hiding;

    /// <summary>
    /// Görev çubuğu ikonuna tıklamak flyout'u önce odak kaybıyla kapatır, ardından tık gelir.
    /// Bu tıkın flyout'u yeniden açmasını engellemek için kısa süre yok sayılır.
    /// </summary>
    public bool WasJustAutoHidden => Environment.TickCount64 - _lastAutoHideTick < 350;

    internal void ShowFlyout(RECT? iconRect)
    {
        _showTick = Environment.TickCount64;
        _hiding = false;

        ApplyFlyoutTheme();

        CardTransform.BeginAnimation(TranslateTransform.YProperty, null);
        FlyoutCard.BeginAnimation(OpacityProperty, null);

        // Çıpa: tepsi ikonunun merkezi (yoksa imleç).
        POINT anchor;
        if (iconRect is { } r)
        {
            anchor = new POINT { X = (r.Left + r.Right) / 2, Y = (r.Top + r.Bottom) / 2 };
        }
        else
        {
            User32.GetCursorPos(out anchor);
        }

        var area = ScreenHelper.GetMonitorAt(anchor.X, anchor.Y);

        FlyoutCard.Opacity = 0;
        if (!IsVisible) Show();
        UpdateLayout();

        // Gerçek boyuta göre en yakın köşeye hizala (DPI duyarlı)
        var dpi = VisualTreeHelper.GetDpi(this);
        var (w, h) = ScreenHelper.GetPixelSize(this);
        int gap = (int)Math.Round(GapDip * dpi.DpiScaleX);

        bool right = anchor.X >= area.Work.Left + area.Work.Width / 2;
        bool bottom = anchor.Y >= area.Work.Top + area.Work.Height / 2;

        int x = right ? area.Work.Right - w - gap : area.Work.Left + gap;
        int y = bottom ? area.Work.Bottom - h - gap : area.Work.Top + gap;

        // Hem WPF DIP koordinatlarını hem Win32 pencere konumunu tam ayarla
        Left = x / dpi.DpiScaleX;
        Top = y / dpi.DpiScaleY;
        ScreenHelper.MoveWindow(this, x, y);

        _slideDirection = bottom ? 1 : -1;

        // Akıcı açılış: içerik CardTransform üzerinden kayar (pencere koordinatı oynamaz, titreme olmaz)
        double startY = _slideDirection * SlideDistanceDip;
        CardTransform.Y = startY;

        var slide = new DoubleAnimation(startY, 0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = EaseOut,
        };
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = EaseOut,
        };

        CardTransform.BeginAnimation(TranslateTransform.YProperty, slide);
        FlyoutCard.BeginAnimation(OpacityProperty, fade);

        // Dışarı tıklandığında anında kapanması için global fare dinleyicisini başlat
        StartMouseHook();

        // Odak kaybında kapanabilmesi ve aktif kalması için ön plana al
        var handle = new WindowInteropHelper(this).EnsureHandle();
        User32.SetForegroundWindow(handle);
        Activate();
        Focus();
    }

    internal void HideFlyout()
    {
        if (!IsVisible || _hiding) return;
        _hiding = true;

        StopMouseHook();

        var duration = TimeSpan.FromMilliseconds(130);
        var fade = new DoubleAnimation(FlyoutCard.Opacity, 0, duration) { EasingFunction = EaseIn };
        var slide = new DoubleAnimation(CardTransform.Y, _slideDirection * SlideDistanceDip, duration)
        {
            EasingFunction = EaseIn,
        };

        fade.Completed += (_, _) =>
        {
            if (!_hiding) return;
            _hiding = false;
            CardTransform.BeginAnimation(TranslateTransform.YProperty, null);
            FlyoutCard.BeginAnimation(OpacityProperty, null);
            Hide();
            MemoryService.ScheduleTrim();
        };

        CardTransform.BeginAnimation(TranslateTransform.YProperty, slide);
        FlyoutCard.BeginAnimation(OpacityProperty, fade);
    }

    private void StartMouseHook()
    {
        if (_mouseHook != IntPtr.Zero) return;
        _mouseHookProc = HookCallback;
        var hMod = Kernel32.GetModuleHandle(null);
        _mouseHook = User32.SetWindowsHookEx(User32.WH_MOUSE_LL, _mouseHookProc, hMod, 0);
    }

    private void StopMouseHook()
    {
        if (_mouseHook != IntPtr.Zero)
        {
            User32.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
            _mouseHookProc = null;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsOpen)
        {
            // Açılış anındaki tıklama hareketini (ilk 300 ms) yok say; sadece açıldıktan sonraki dış tıklamaları yakala
            if (Environment.TickCount64 - _showTick >= 300)
            {
                int msg = (int)wParam;
                if (msg is User32.WM_LBUTTONDOWN or User32.WM_RBUTTONDOWN or User32.WM_MBUTTONDOWN
                        or User32.WM_NCLBUTTONDOWN or User32.WM_NCRBUTTONDOWN)
                {
                    var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    var handle = new WindowInteropHelper(this).Handle;
                    if (handle != IntPtr.Zero && User32.GetWindowRect(handle, out var rect))
                    {
                        if (!rect.Contains(hookStruct.pt.X, hookStruct.pt.Y))
                        {
                            // Dışarı tıklandı: flyout'u doğrudan kapat
                            _lastAutoHideTick = Environment.TickCount64;
                            Dispatcher.BeginInvoke(HideFlyout);
                        }
                    }
                }
            }
        }
        return User32.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private void ApplyFlyoutTheme()
    {
        bool dark = ApplicationThemeManager.GetAppTheme() != ApplicationTheme.Light;
        var bg = dark ? Color.FromRgb(0x23, 0x23, 0x23) : Color.FromRgb(0xF3, 0xF3, 0xF3);
        var border = dark ? Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1F, 0x00, 0x00, 0x00);
        var fg = dark ? Colors.White : Color.FromRgb(0x1A, 0x1A, 0x1A);

        Brush accentBrush;
        try
        {
            accentBrush = ApplicationAccentColorManager.SystemAccentBrush;
        }
        catch
        {
            accentBrush = dark
                ? new SolidColorBrush(Color.FromRgb(0x00, 0x5F, 0xB8))
                : new SolidColorBrush(Color.FromRgb(0x00, 0x67, 0xC0));
        }

        var unfilledBrush = dark
            ? new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8D))
            : new SolidColorBrush(Color.FromRgb(0xC4, 0xC4, 0xC4));

        Resources["SystemAccentBrush"] = accentBrush;
        Resources["SliderTrackUnfilledBrush"] = unfilledBrush;
        Resources["SliderThumbCenterBrush"] = dark ? Brushes.White : Brushes.White;

        Background = Brushes.Transparent;
        FlyoutCard.Background = new SolidColorBrush(bg);
        FlyoutCard.BorderBrush = new SolidColorBrush(border);
        var fgBrush = new SolidColorBrush(fg);
        Foreground = fgBrush;
        TextElement.SetForeground(FlyoutCard, fgBrush);
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (!IsOpen) return;

        // Açılışın hemen sonrasındaki (görev çubuğu fare bırakma, aktivasyon geçişleri)
        // anlık odak kayıplarında pencerenin hemen kapanmasını engelle
        if (Environment.TickCount64 - _showTick < 450) return;

        _lastAutoHideTick = Environment.TickCount64;
        HideFlyout();
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HideFlyout();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Kullanıcı kaydırıcıyı oynattıkça tetiklenir. Bekleme yok: değer yalnızca arka plan kuyruğuna yazılır,
    /// donanım iletişimi (DDC/CI) UI thread'i dışında async olarak yürür.
    /// </summary>
    private void OnSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is Slider { IsLoaded: true, DataContext: MonitorViewModel vm })
        {
            _controller.SetPercent(vm, (int)Math.Round(e.NewValue));
        }
    }

    /// <summary>
    /// Kaydırıcı veya monitör satırı üzerinde fare tekerleği çevrildiğinde parlaklığı adım kadar değiştirir.
    /// Tıklamaya gerek kalmadan hover anında çalışır.
    /// </summary>
    private void OnSliderMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MonitorViewModel vm })
        {
            int step = _getStepPercent();
            int delta = e.Delta > 0 ? step : -step;
            _controller.SetPercent(vm, vm.Percent + delta);
            e.Handled = true;
        }
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => _ = _controller.RefreshAsync();

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        HideFlyout();
        _openSettings();
    }

    private void UpdateEmptyState()
    {
        EmptyText.Visibility = _controller.HasMonitors ? Visibility.Collapsed : Visibility.Visible;
        EmptyText.Text = _controller.IsRefreshing
            ? "Monitörler aranıyor…"
            : "DDC/CI destekleyen harici monitör bulunamadı. Monitör menüsünde DDC/CI seçeneğinin açık olduğundan emin olun.";
    }
}