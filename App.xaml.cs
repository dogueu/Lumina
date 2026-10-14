using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using Lumina.Models;
using Lumina.Native;
using Lumina.Services;
using Lumina.Views;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Lumina;

/// <summary>
/// Uygulama kökü (composition root). Pencere açılmaz; her şey tepsi ikonu, global kısayollar
/// ve tepsi tekerleği olaylarıyla yönetilir. Boşta hiçbir timer/polling çalışmaz.
/// </summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\Lumina.SingleInstance.6F1B7E52";

    private Mutex? _mutex;
    private bool _ownsMutex;

    private SettingsService _settings = null!;
    private StartupService _startup = null!;
    private MessageWindow _messageWindow = null!;
    private BrightnessController _brightness = null!;
    private HotkeyService _hotkeys = null!;
    private TrayIconService _tray = null!;

    private MainWindow? _flyout;
    private SettingsWindow? _settingsWindow;
    private OsdWindow? _osd;
    private ContextMenu? _trayMenu;

    private DispatcherTimer? _tooltipTimer;
    private int _tooltipPercent;
    private string? _hotkeyWarning;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out _ownsMutex);
        if (!_ownsMutex)
        {
            // Zaten çalışıyor (aynı kısayolları iki kez kaydedemeyiz).
            _mutex.Dispose();
            _mutex = null;
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Debug.WriteLine($"[Lumina] Yakalanmamış hata: {args.Exception}");
            args.Handled = true;
        };

        ApplicationThemeManager.ApplySystemTheme();

        _settings = new SettingsService();
        _settings.Load();
        _startup = new StartupService();
        SyncStartupRegistration();

        _messageWindow = new MessageWindow();
        _messageWindow.AddHook(OnWindowMessage);

        _brightness = new BrightnessController();
        _brightness.SetCustomNames(_settings.Current.MonitorCustomNames);
        _brightness.SetSavedBrightness(_settings.Current.MonitorBrightness, (id, val) =>
        {
            _settings.Current.MonitorBrightness[id] = val;
            _settings.ScheduleSave();
        });
        _brightness.LevelChanged += OnLevelChanged;
        _brightness.Adjusted += OnAdjusted;

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

        _ = _brightness.RefreshAsync();
        MemoryService.ScheduleTrim(10);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tooltipTimer?.Stop();
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

    // ---------------------------------------------------------------- Tepsi etkileşimi

    private long _lastTrayClickTick;

    private void OnTrayLeftClick()
    {
        long now = Environment.TickCount64;
        if (now - _lastTrayClickTick < 350) return;
        _lastTrayClickTick = now;

        if (_flyout is null)
        {
            var prevMain = MainWindow;
            _flyout = new MainWindow(_brightness, OpenSettings, () => _settings.Current.StepPercent);
            MainWindow = prevMain;
        }

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
        UpdateTrayMenuTheme(_trayMenu);
        _trayMenu.Placement = PlacementMode.MousePoint;
        _trayMenu.StaysOpen = false;
        _trayMenu.IsOpen = true;
    }

    private void UpdateTrayMenuTheme(ContextMenu menu)
    {
        bool dark = ApplicationThemeManager.GetAppTheme() != ApplicationTheme.Light;
        var fg = dark ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));

        menu.Resources["MenuCardBackground"] = new SolidColorBrush(dark ? Color.FromRgb(0x28, 0x28, 0x28) : Color.FromRgb(0xF9, 0xF9, 0xF9));
        menu.Resources["MenuCardBorder"] = new SolidColorBrush(dark ? Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x18, 0x00, 0x00, 0x00));
        menu.Resources["TrayMenuForeground"] = fg;
        menu.Resources["TrayMenuHoverBackground"] = new SolidColorBrush(dark ? Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x0C, 0x00, 0x00, 0x00));
        menu.Resources["TrayMenuPressedBackground"] = new SolidColorBrush(dark ? Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x06, 0x00, 0x00, 0x00));
        menu.Resources["TrayMenuSeparatorBrush"] = new SolidColorBrush(dark ? Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1A, 0x00, 0x00, 0x00));
    }

    private ContextMenu BuildTrayMenu()
    {
        var menu = new ContextMenu();
        if (TryFindResource("Win11TrayContextMenuStyle") is Style style)
            menu.Style = style;

        menu.SetValue(System.Windows.Controls.Primitives.Popup.PopupAnimationProperty, System.Windows.Controls.Primitives.PopupAnimation.Fade);
        UpdateTrayMenuTheme(menu);

        menu.Items.Add(CreateMenuItem("Ayarlar", SymbolRegular.Settings24, OpenSettings));
        menu.Items.Add(CreateMenuItem("Monitörleri yenile", SymbolRegular.ArrowSync24, () => _ = _brightness.RefreshAsync()));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("Çıkış", SymbolRegular.Dismiss24, () => Shutdown()));
        menu.Closed += (_, _) => MemoryService.ScheduleTrim();
        return menu;
    }

    private static System.Windows.Controls.MenuItem CreateMenuItem(string header, SymbolRegular symbol, Action onClick)
    {
        var icon = new SymbolIcon
        {
            Symbol = symbol,
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center
        };
        icon.SetResourceReference(Control.ForegroundProperty, "TrayMenuForeground");

        var item = new System.Windows.Controls.MenuItem
        {
            Header = header,
            Icon = icon
        };
        item.SetResourceReference(Control.ForegroundProperty, "TrayMenuForeground");
        item.Click += (_, _) => onClick();
        return item;
    }

    // ---------------------------------------------------------------- Pencereler

    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            if (_settingsWindow.WindowState == WindowState.Minimized)
                _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
            var handle = new WindowInteropHelper(_settingsWindow).Handle;
            if (handle != IntPtr.Zero) User32.SetForegroundWindow(handle);
            return;
        }

        _settingsWindow = new SettingsWindow(_settings.Current.Clone(), _brightness.Monitors, ApplySettings, _hotkeyWarning);
        MainWindow = _settingsWindow;
        _settingsWindow.Closing += (_, _) =>
        {
            if (_settingsWindow.WindowState == WindowState.Normal)
            {
                _settings.Current.SettingsWindowWidth = _settingsWindow.ActualWidth;
                _settings.Current.SettingsWindowHeight = _settingsWindow.ActualHeight;
                _settings.Save(_settings.Current);
            }
        };
        _settingsWindow.Closed += (_, _) =>
        {
            if (ReferenceEquals(MainWindow, _settingsWindow))
                MainWindow = null;
            _settingsWindow = null;
            MemoryService.ScheduleTrim();
        };
        _settingsWindow.Show();
        _settingsWindow.Activate();
        var h = new WindowInteropHelper(_settingsWindow).Handle;
        if (h != IntPtr.Zero) User32.SetForegroundWindow(h);
    }

    /// <summary>Ayarlar penceresinden gelen yeni ayarları uygular; hata varsa mesajını döndürür.</summary>
    private string? ApplySettings(AppSettings settings)
    {
        var previous = _settings.Current;

        var result = _hotkeys.Register(settings.IncreaseHotkey, settings.DecreaseHotkey);
        if (!result.Success)
        {
            string error = _hotkeys.LastError ?? "Kısayollar kaydedilemedi.";
            _hotkeys.Register(previous.IncreaseHotkey, previous.DecreaseHotkey); // eski haline döndür
            _hotkeyWarning = _hotkeys.LastError;
            return error;
        }

        _hotkeyWarning = null;

        string? startupError = null;
        try
        {
            _startup.SetEnabled(settings.RunAtStartup);
        }
        catch (Exception ex)
        {
            settings.RunAtStartup = _startup.IsEnabled;
            startupError = $"Başlangıç ayarı değiştirilemedi: {ex.Message}";
        }

        if (!_settings.Save(settings)) return "Ayarlar dosyaya yazılamadı.";
        _brightness.SetCustomNames(settings.MonitorCustomNames);
        return startupError;
    }

    private void SyncStartupRegistration()
    {
        try
        {
            // Uygulama taşındıysa Run anahtarındaki yol güncel tutulur.
            if (_settings.Current.RunAtStartup && !_startup.IsPathCurrent)
                _startup.SetEnabled(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Lumina] Başlangıç kaydı eşitlenemedi: {ex.Message}");
        }
    }

    private void OnAdjusted(int percent)
    {
        if (!_settings.Current.ShowOsd) return;
        if (_flyout?.IsOpen == true) return; // kaydırıcılar zaten görünür

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
                Interval = TimeSpan.FromMilliseconds(300),
            };
            _tooltipTimer.Tick += (_, _) =>
            {
                _tooltipTimer!.Stop();
                _tray.SetTooltip(_brightness.HasMonitors ? $"Lumina — Parlaklık: {_tooltipPercent}%" : "Lumina");
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
                _brightness.ScheduleRefresh(TimeSpan.FromMilliseconds(1500));
                break;

            case User32.WM_POWERBROADCAST when (int)wParam == User32.PBT_APMRESUMEAUTOMATIC:
                _brightness.ScheduleRefresh(TimeSpan.FromSeconds(4));
                break;

            case User32.WM_SETTINGCHANGE when lParam != IntPtr.Zero
                                              && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet":
                ApplicationThemeManager.ApplySystemTheme();
                _tray.RefreshIcon();
                break;
        }

        return IntPtr.Zero;
    }
}
