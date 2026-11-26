using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Lumina.Models;
using Lumina.Native;
using Lumina.Services;
using Lumina.Views;

namespace Lumina;

/// <summary>
/// Uygulama kökü (composition root). Pencere açılmaz; her şey tepsi ikonu, global kısayollar
/// ve tepsi tekerleği olaylarıyla yönetilir. Boşta hiçbir timer/polling çalışmaz.
/// </summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\Lumina.SingleInstance.6F1B7E52";
    private const int TrayClickDebounceMs = 350;
    private const int TooltipDebounceMs = 300;
    private const int DisplayChangeRefreshDelayMs = 500;
    private const int PowerResumeRefreshDelayMs = 1500;
    private const int StartupTrimDelaySeconds = 10;

    private Mutex? _mutex;
    private bool _ownsMutex;

    private SettingsService _settings = null!;
    private StartupService _startup = null!;
    private MessageWindow _messageWindow = null!;
    private BrightnessController _brightness = null!;
    private HotkeyService _hotkeys = null!;
    private TrayIconService _tray = null!;

    private MainWindow? _flyout;
    private OsdWindow? _osd;
    private ContextMenu? _trayMenu;

    private DispatcherTimer? _tooltipTimer;
    private int _tooltipPercent;
    private string? _hotkeyWarning;
    private long _lastTrayClickTick;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out _ownsMutex);
        if (!_ownsMutex)
        {
            _mutex.Dispose();
            _mutex = null;
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            AppDiagnostics.RecordCrash("DispatcherUnhandledException", args.Exception);
            if (!AppDiagnostics.IsFatal(args.Exception))
                args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppDiagnostics.RecordCrash("AppDomain.UnhandledException", args.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppDiagnostics.RecordCrash("TaskScheduler.UnobservedTaskException", args.Exception);
            args.SetObserved();
        };

        SessionEnding += (_, _) =>
        {
            _settings?.Flush();
            Shutdown();
        };

        _settings = new SettingsService();
        _settings.Load();

        ThemeService.Apply(
            ThemeService.Parse(_settings.Current.Appearance),
            _settings.Current.GlassOpacity,
            _settings.Current.GlassBlur);

        // Donanım parlaklık eşitlemesini beklemeden ilk milisaniyede arka planda başlat:
        _brightness = new BrightnessController();
        _brightness.SetCustomNames(_settings.Current.MonitorCustomNames);
        _brightness.SetExcludedHotkeyMonitors(_settings.Current.ExcludedHotkeyMonitors);
        _brightness.SetMonitorOrder(_settings.Current.MonitorOrder);
        _brightness.SetSavedBrightness(_settings.Current.MonitorBrightness, (id, val) =>
        {
            _settings.Current.MonitorBrightness[id] = val;
            _settings.ScheduleSave();
        });
        _brightness.LevelChanged += OnLevelChanged;
        _brightness.Adjusted += OnAdjusted;
        _ = _brightness.RefreshAsync();

        _startup = new StartupService();
        SyncStartupRegistration();

        _messageWindow = new MessageWindow();
        _messageWindow.AddHook(OnWindowMessage);

        _hotkeys = new HotkeyService(_messageWindow);
        _hotkeys.IncreaseRequested += () => _brightness.Adjust(+_settings.Current.StepPercent);
        _hotkeys.DecreaseRequested += () => _brightness.Adjust(-_settings.Current.StepPercent);
        _hotkeys.Register(_settings.Current.IncreaseHotkey, _settings.Current.DecreaseHotkey);
        _hotkeyWarning = _hotkeys.LastError;

        _tray = new TrayIconService(_messageWindow);
        _tray.LeftClick += OnTrayLeftClick;
        _tray.RightClick += ShowTrayMenu;
        _tray.Wheel += steps => _brightness.Adjust(steps * _settings.Current.StepPercent);
        _tray.SetTooltip("Lumina");
        _tray.Show();

        MemoryService.ScheduleTrim(StartupTrimDelaySeconds);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        MemoryService.Stop();
        _tooltipTimer?.Stop();

        _flyout?.Close();
        _osd?.Close();

        _settings?.Flush();
        _tray?.Dispose();
        _hotkeys?.Dispose();
        _brightness?.Dispose();
        _messageWindow?.Dispose();

        if (_mutex is not null)
        {
            if (_ownsMutex) _mutex.ReleaseMutex();
            _mutex.Dispose();
        }

        base.OnExit(e);
    }

    // ---------------------------------------------------------------- Tepsi etkileşimi & Flyout

    private MainWindow CreateFlyout()
    {
        var prevMain = MainWindow;
        var w = new MainWindow(_brightness, _settings, UpdateHotkeys, UpdateStartup, () => _hotkeyWarning);
        MainWindow = prevMain;
        return w;
    }

    private void OnTrayLeftClick()
    {
        long now = Environment.TickCount64;
        if (now - _lastTrayClickTick < TrayClickDebounceMs) return;
        _lastTrayClickTick = now;

        _flyout ??= CreateFlyout();

        if (_flyout.IsOpen)
        {
            _flyout.HideFlyout();
            return;
        }

        if (_flyout.WasJustAutoHidden) return;

        if (!_brightness.HasMonitors && !_brightness.IsRefreshing)
            _ = _brightness.RefreshAsync();

        _flyout.ShowFlyout(_tray.TryGetIconRect(out var rect) ? rect : null);
    }

    private void ShowTrayMenu()
    {
        _flyout?.HideFlyout();

        User32.SetForegroundWindow(_messageWindow.Handle);

        _trayMenu ??= BuildTrayMenu();
        _trayMenu.Placement = PlacementMode.MousePoint;
        _trayMenu.StaysOpen = false;
        _trayMenu.IsOpen = true;
    }

    private ContextMenu BuildTrayMenu()
    {
        var menu = new ContextMenu();
        if (TryFindResource("Win11TrayContextMenuStyle") is Style style)
            menu.Style = style;

        menu.SetValue(Popup.PopupAnimationProperty, PopupAnimation.Fade);

        menu.Items.Add(CreateMenuItem("Ayarlar", Icons.Sliders, OpenSettings));
        menu.Items.Add(CreateMenuItem("Monitörleri yenile", Icons.Refresh, () => _ = _brightness.RefreshAsync()));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("Çıkış", Icons.Dismiss, () => Shutdown()));
        menu.Closed += (_, _) => MemoryService.ScheduleTrim();
        return menu;
    }

    private static MenuItem CreateMenuItem(string header, Geometry iconGeometry, Action onClick)
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = iconGeometry,
            Width = 20,
            Height = 20,
        };
        path.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Text.Primary");

        var iconBox = new Viewbox
        {
            Width = 15,
            Height = 15,
            VerticalAlignment = VerticalAlignment.Center,
            Child = path,
        };

        var item = new MenuItem
        {
            Header = header,
            Icon = iconBox,
        };
        item.Click += (_, _) => onClick();
        return item;
    }

    // ---------------------------------------------------------------- Ayar geri çağrıları

    private void OpenSettings()
    {
        _flyout ??= CreateFlyout();

        if (!_brightness.HasMonitors && !_brightness.IsRefreshing)
            _ = _brightness.RefreshAsync();

        if (_flyout.IsOpen)
            _flyout.OpenSettings();
        else
            _flyout.ShowFlyout(_tray.TryGetIconRect(out var rect) ? rect : null, openInSettings: true);
    }

    private string? UpdateHotkeys(HotkeyGesture increase, HotkeyGesture decrease)
    {
        var previous = _settings.Current;
        var result = _hotkeys.Register(increase, decrease);
        if (!result.Success)
        {
            string error = _hotkeys.LastError ?? "Kısayollar kaydedilemedi.";
            _hotkeys.Register(previous.IncreaseHotkey, previous.DecreaseHotkey);
            _hotkeyWarning = _hotkeys.LastError;
            return error;
        }

        _hotkeyWarning = null;
        return null;
    }

    private string? UpdateStartup(bool enabled)
    {
        try
        {
            _startup.SetEnabled(enabled);
            _settings.Current.RunAtStartup = enabled;
            _settings.Save(_settings.Current);
            return null;
        }
        catch (Exception ex)
        {
            _settings.Current.RunAtStartup = _startup.IsEnabled;
            return $"Başlangıç ayarı değiştirilemedi: {ex.Message}";
        }
    }

    private void SyncStartupRegistration()
    {
        try
        {
            // Uygulama taşındıysa veya başlangıç gecikmesi kapatılmamışsa kayıt güncel tutulur.
            if (_settings.Current.RunAtStartup && !_startup.IsPathCurrent)
                _startup.SetEnabled(true);
        }
        catch (Exception ex)
        {
            AppDiagnostics.Trace($"Başlangıç kaydı eşitlenemedi: {ex.Message}");
        }
    }

    private void OnAdjusted(int percent)
    {
        if (!_settings.Current.ShowOsd) return;
        if (_flyout?.IsOpen == true && !_flyout.IsShowingSettings) return;

        if (_osd is null)
        {
            var prevMain = MainWindow;
            _osd = new OsdWindow();
            MainWindow = prevMain;
        }
        _osd.ShowLevel(percent);
    }

    // ---------------------------------------------------------------- Tepsi ipucu (debounce)

    private void OnLevelChanged(int percent)
    {
        _tooltipPercent = percent;
        if (_tooltipTimer is null)
        {
            _tooltipTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(TooltipDebounceMs),
            };
            _tooltipTimer.Tick += (_, _) =>
            {
                _tooltipTimer!.Stop();
                _tray.SetTooltip(_brightness.HasActiveHotkeyMonitors ? $"Lumina — Parlaklık: {_tooltipPercent}" : "Lumina");
            };
        }

        _tooltipTimer.Stop();
        _tooltipTimer.Start();
    }

    // ---------------------------------------------------------------- Sistem olayları

    private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case User32.WM_DISPLAYCHANGE:
                // Monitör takıldı/çıkarıldı: DDC/CI hazır olması için kısa süre bekle.
                _brightness.ScheduleRefresh(TimeSpan.FromMilliseconds(DisplayChangeRefreshDelayMs));
                break;

            case User32.WM_POWERBROADCAST when (int)wParam == User32.PBT_APMRESUMEAUTOMATIC:
                _brightness.ScheduleRefresh(TimeSpan.FromMilliseconds(PowerResumeRefreshDelayMs));
                break;

            case User32.WM_SETTINGCHANGE when lParam != IntPtr.Zero
                                              && Marshal.PtrToStringUni(lParam) is "ImmersiveColorSet" or "WindowsThemeElement":
                ThemeService.Apply(
                    ThemeService.Parse(_settings.Current.Appearance),
                    _settings.Current.GlassOpacity,
                    _settings.Current.GlassBlur);
                _tray.RefreshIcon();
                break;
        }

        return IntPtr.Zero;
    }
}
