using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Lumina.Models;
using Lumina.Native;
using Lumina.Services;
using Lumina.ViewModels;

namespace Lumina;

/// <summary>
/// Sistem tepsisi flyout'u: ana monitör listesi ve aynı panel içinde sağdan kayarak açılan Ayarlar görünümü.
/// </summary>
public partial class MainWindow : Window
{
    private const int AutoHideGuardMs = 350;
    private const int ShowHookDelayMs = 300;
    private const int ShowDeactivateDelayMs = 450;
    private const int ShowSlideOffsetDip = 16;
    private const int HideSlideOffsetDip = 10;
    private const double CornerRadiusDip = 8.0;
    private const int TrayAnchorGapDip = 12;

    private readonly BrightnessController _controller;
    private readonly SettingsService _settings;
    private readonly Func<HotkeyGesture, HotkeyGesture, string?> _updateHotkeys;
    private readonly Func<bool, string?> _updateStartup;
    private readonly Func<string?> _getHotkeyWarning;

    private BlurUnderlay? _blur;
    private RECT? _lastAnchorRect;
    private IntPtr _hwnd;
    private IntPtr _mouseHook;
    private LowLevelMouseProc? _mouseHookProc;
    private bool _hiding;
    private bool _closing;
    private bool _loadingSettings;
    private bool _reorderingMonitors;
    private int _animGen;
    private long _lastAutoHideTick;
    private long _showTick;

    public bool IsOpen => IsVisible && !_hiding;
    public bool IsShowingSettings => SettingsView.Visibility == Visibility.Visible;

    /// <summary>
    /// Görev çubuğu ikonuna tıklamak flyout'u önce odak kaybıyla kapatır, ardından tık gelir.
    /// Bu tıkın flyout'u yeniden açmasını engellemek için kısa süre yok sayılır.
    /// </summary>
    public bool WasJustAutoHidden => Environment.TickCount64 - _lastAutoHideTick < AutoHideGuardMs;

    internal MainWindow(
        BrightnessController controller,
        SettingsService settings,
        Func<HotkeyGesture, HotkeyGesture, string?> updateHotkeys,
        Func<bool, string?> updateStartup,
        Func<string?> getHotkeyWarning)
    {
        _controller = controller;
        _settings = settings;
        _updateHotkeys = updateHotkeys;
        _updateStartup = updateStartup;
        _getHotkeyWarning = getHotkeyWarning;

        InitializeComponent();
        AllowsTransparency = true;

        DataContext = controller;
        Icon = IconFactory.RenderSun(64, Colors.White);

        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = ver is not null ? $"Lumina {ver.Major}.{ver.Minor}.{ver.Build}" : "Lumina";

        WireSettings();
        BackButton.Click += (_, _) => ShowSettings(false, animate: true);

        _controller.PropertyChanged += OnControllerPropertyChanged;
        _controller.Monitors.CollectionChanged += OnMonitorsChanged;

        Closing += (_, _) =>
        {
            _closing = true;
            StopMouseHook();
        };
        Deactivated += OnDeactivated;
        Closed += (_, _) =>
        {
            StopMouseHook();
            ThemeService.Changed -= OnThemeChanged;
            _controller.PropertyChanged -= OnControllerPropertyChanged;
            _controller.Monitors.CollectionChanged -= OnMonitorsChanged;
        };
        SizeChanged += (_, _) =>
        {
            if (IsVisible) PlaceNearTray();
        };
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).EnsureHandle();
            ThemeService.MakeToolWindow(this);
            _blur = BlurUnderlay.Attach(this);
            ApplyTheme();
        };
        ThemeService.Changed += OnThemeChanged;

        RenderMainState();
    }

    // ---------------------------------------------------------------- Yaşam döngüsü & konum

    private void OnThemeChanged() => Dispatcher.BeginInvoke(() =>
    {
        if (_closing) return;
        ApplyTheme();
        RenderMainState();
        if (IsShowingSettings) LoadSettings();
    });

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RenderMainState();
    }

    private void OnMonitorsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RenderMainState();
        if (IsShowingSettings && !_loadingSettings && !_reorderingMonitors)
            PopulateMonitorSettings();
    }

    internal void ShowFlyout(RECT? iconRect, bool openInSettings = false)
    {
        if (_closing) return;

        _animGen++;
        _showTick = Environment.TickCount64;
        _hiding = false;
        _lastAnchorRect = iconRect;

        ShowSettings(openInSettings, animate: false);
        RenderMainState();
        ApplyTheme();

        Slide.BeginAnimation(TranslateTransform.YProperty, null);
        Body.BeginAnimation(OpacityProperty, null);

        Body.Opacity = 0;
        Slide.Y = ShowSlideOffsetDip;
        if (!IsVisible) Show();
        UpdateLayout();
        PlaceNearTray();

        Motion.Run(Slide, TranslateTransform.YProperty, ShowSlideOffsetDip, 0, 420, Motion.Out);
        Motion.Run(Body, OpacityProperty, 0, 1, 260, Motion.Out);

        StartMouseHook();

        if (_hwnd == IntPtr.Zero)
            _hwnd = new WindowInteropHelper(this).EnsureHandle();
        User32.SetForegroundWindow(_hwnd);
        Activate();
        Focus();
    }

    internal void HideFlyout()
    {
        if (!IsVisible || _hiding || _closing) return;
        StopMouseHook();

        if (!Motion.Enabled)
        {
            Hide();
            MemoryService.ScheduleTrim();
            return;
        }

        _hiding = true;
        int gen = ++_animGen;
        Motion.Run(Slide, TranslateTransform.YProperty, null, HideSlideOffsetDip, 150, Motion.In);
        Motion.Run(Body, OpacityProperty, null, 0, 150, Motion.In, () =>
        {
            if (!_hiding || _closing || gen != _animGen) return;
            _hiding = false;
            Slide.BeginAnimation(TranslateTransform.YProperty, null);
            Body.BeginAnimation(OpacityProperty, null);
            Hide();
            MemoryService.ScheduleTrim();
        });
    }

    internal void OpenSettings() => ShowSettings(true, animate: IsVisible);

    private void ShowSettings(bool on, bool animate)
    {
        if (on) LoadSettings();

        bool changed = (SettingsView.Visibility == Visibility.Visible) != on;
        MainView.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        SettingsView.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

        if (!on) RenderMainState();
        if (animate && changed && IsVisible)
            Motion.EnterView(on ? SettingsView : MainView, on ? 1 : -1);
    }

    private void PlaceNearTray()
    {
        POINT anchor;
        if (_lastAnchorRect is { } r)
        {
            anchor = new POINT { X = (r.Left + r.Right) / 2, Y = (r.Top + r.Bottom) / 2 };
        }
        else
        {
            User32.GetCursorPos(out anchor);
        }

        var area = ScreenHelper.GetMonitorAt(anchor.X, anchor.Y);
        var dpi = VisualTreeHelper.GetDpi(this);

        double workH = area.Work.Height / dpi.DpiScaleY;
        Scroller.MaxHeight = Math.Max(200, workH - 160);
        SettingsScroller.MaxHeight = Math.Max(200, workH - 150);

        UpdateLayout();
        var (w, h) = ScreenHelper.GetPixelSize(this);
        int gap = (int)Math.Round(TrayAnchorGapDip * dpi.DpiScaleX);

        bool right = anchor.X >= area.Work.Left + area.Work.Width / 2;
        bool bottom = anchor.Y >= area.Work.Top + area.Work.Height / 2;

        int x = right ? area.Work.Right - w - gap : area.Work.Left + gap;
        int y = bottom ? area.Work.Bottom - h - gap : area.Work.Top + gap;

        Left = x / dpi.DpiScaleX;
        Top = y / dpi.DpiScaleY;
        ScreenHelper.MoveWindow(this, x, y);

        _blur?.Update(CornerRadiusDip * dpi.DpiScaleX, ThemeService.BlurOpacity);
    }

    private void ApplyTheme()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        _blur?.Update(CornerRadiusDip * dpi.DpiScaleX, ThemeService.BlurOpacity);
    }

    // ---------------------------------------------------------------- Ana görünüm durumu

    private void RenderMainState()
    {
        bool refreshing = _controller.IsRefreshing;
        bool hasMonitors = _controller.HasMonitors;

        ReadingPill.Visibility = refreshing ? Visibility.Visible : Visibility.Collapsed;
        RefreshButton.Visibility = refreshing ? Visibility.Collapsed : Visibility.Visible;

        if (refreshing)
        {
            Spin.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever });
            SubtitleText.Text = "Monitörler taranıyor…";
            SubtitleText.Visibility = Visibility.Visible;
        }
        else
        {
            Spin.BeginAnimation(RotateTransform.AngleProperty, null);
            SubtitleText.Visibility = Visibility.Collapsed;
        }

        EmptyView.Visibility = hasMonitors ? Visibility.Collapsed : Visibility.Visible;
        if (!hasMonitors)
        {
            EmptyBody.Text = refreshing
                ? "Bağlı monitörler algılanıyor…"
                : "DDC/CI destekleyen harici monitör bulunamadı. Monitör menüsünde DDC/CI seçeneğinin açık olduğundan emin olun.";
            EmptyRefresh.Visibility = refreshing ? Visibility.Collapsed : Visibility.Visible;
        }

        int total = _controller.Monitors.Count;
        int active = _controller.Monitors.Count(m => m.IsHotkeyEnabled);

        FooterText.Text = total == 0
            ? (refreshing ? "Taranıyor…" : "Monitör bağlı değil")
            : $"{total} monitör bağlı";

        LiveLegend.Visibility = active > 0 ? Visibility.Visible : Visibility.Collapsed;
        LiveLegendText.Text = $"{active} kısayol aktif";
    }

    // ---------------------------------------------------------------- Fare & Kaydırıcı olayları

    private void StartMouseHook()
    {
        if (_mouseHook != IntPtr.Zero) return;
        _mouseHookProc = HookCallback;
        var hMod = Kernel32.GetModuleHandle(null);
        _mouseHook = User32.SetWindowsHookEx(User32.WH_MOUSE_LL, _mouseHookProc, hMod, 0);
        if (_mouseHook == IntPtr.Zero)
        {
            _mouseHookProc = null;
            AppDiagnostics.Trace($"Flyout fare hook'u kurulamadı: {Marshal.GetLastWin32Error()}");
        }
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
        if (nCode >= 0 && IsOpen && !_closing && !Dispatcher.HasShutdownStarted
            && Environment.TickCount64 - _showTick >= ShowHookDelayMs)
        {
            try
            {
                int msg = (int)wParam;
                if (msg is User32.WM_LBUTTONDOWN or User32.WM_RBUTTONDOWN or User32.WM_MBUTTONDOWN
                    or User32.WM_NCLBUTTONDOWN or User32.WM_NCRBUTTONDOWN)
                {
                    var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    if (_hwnd != IntPtr.Zero && User32.GetWindowRect(_hwnd, out var rect))
                    {
                        if (!rect.Contains(hookStruct.pt.X, hookStruct.pt.Y))
                        {
                            _lastAutoHideTick = Environment.TickCount64;
                            Dispatcher.BeginInvoke(HideFlyout);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppDiagnostics.Trace($"Flyout hook hatası: {ex.Message}");
            }
        }

        return User32.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (_closing || !IsOpen) return;
        if (Environment.TickCount64 - _showTick < ShowDeactivateDelayMs) return;

        _lastAutoHideTick = Environment.TickCount64;
        HideFlyout();
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;

        if (IsShowingSettings)
            ShowSettings(false, animate: true);
        else
            HideFlyout();

        e.Handled = true;
    }

    private void OnSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsOpen || IsShowingSettings) return;
        if (sender is Slider { IsLoaded: true, DataContext: MonitorViewModel vm })
        {
            _controller.SetPercent(vm, (int)Math.Round(e.NewValue));
        }
    }

    private void OnSliderMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MonitorViewModel vm })
        {
            int step = _settings.Current.StepPercent;
            int delta = e.Delta > 0 ? step : -step;
            _controller.SetPercent(vm, vm.Percent + delta);
            e.Handled = true;
        }
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => _ = _controller.RefreshAsync();

    private void OnSettingsClick(object sender, RoutedEventArgs e) => ShowSettings(true, animate: true);
}
