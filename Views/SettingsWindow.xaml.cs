using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Lumina.Models;
using Lumina.Services;
using Lumina.ViewModels;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Lumina.Views;

public partial class SettingsWindow : FluentWindow
{
    private readonly Func<AppSettings, string?> _apply;
    private readonly IReadOnlyList<MonitorViewModel> _monitors;
    private readonly List<Wpf.Ui.Controls.TextBox> _monitorBoxes = new();
    private readonly AppSettings _current;

    private HotkeyGesture _increase;
    private HotkeyGesture _decrease;

    /// <param name="current">Mevcut ayarlar.</param>
    /// <param name="monitors">Algılanan monitör listesi.</param>
    /// <param name="apply">Ayarları uygular; hata varsa kullanıcıya gösterilecek metni, başarıda null döndürür.</param>
    /// <param name="initialWarning">Açılışta gösterilecek uyarı (ör. kısayol çakışması).</param>
    internal SettingsWindow(
        AppSettings current,
        IReadOnlyList<MonitorViewModel> monitors,
        Func<AppSettings, string?> apply,
        string? initialWarning)
    {
        InitializeComponent();

        _current = current;
        _apply = apply;
        _monitors = monitors;
        _increase = current.IncreaseHotkey;
        _decrease = current.DecreaseHotkey;

        if (current.SettingsWindowWidth >= MinWidth && current.SettingsWindowHeight >= MinHeight)
        {
            Width = current.SettingsWindowWidth;
            Height = current.SettingsWindowHeight;
        }

        Icon = IconFactory.RenderSun(64, Colors.White);
        SystemThemeWatcher.Watch(this);

        Load(current);
        if (!string.IsNullOrEmpty(initialWarning)) ShowStatus(initialWarning, isError: true);
    }

    private void Load(AppSettings s)
    {
        StepBox.Value = s.StepPercent;
        StartupToggle.IsChecked = s.RunAtStartup;
        OsdToggle.IsChecked = s.ShowOsd;
        _increase = s.IncreaseHotkey;
        _decrease = s.DecreaseHotkey;
        IncreaseBox.Text = _increase.ToString();
        DecreaseBox.Text = _decrease.ToString();

        PopulateMonitors(s);
    }

    private void PopulateMonitors(AppSettings s)
    {
        MonitorListPanel.Children.Clear();
        _monitorBoxes.Clear();

        if (_monitors.Count == 0)
        {
            NoMonitorsText.Visibility = Visibility.Visible;
            return;
        }

        NoMonitorsText.Visibility = Visibility.Collapsed;

        foreach (var m in _monitors)
        {
            var card = new CardControl { Margin = new Thickness(0, 0, 0, 6) };

            var sp = new StackPanel();
            var title = new System.Windows.Controls.TextBlock
            {
                Text = string.IsNullOrWhiteSpace(m.RawName) ? m.Name : m.RawName,
                FontWeight = FontWeights.SemiBold
            };
            var sub = new System.Windows.Controls.TextBlock
            {
                Text = $"Cihaz ID: {m.Id}",
                Opacity = 0.6,
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 240
            };
            sp.Children.Add(title);
            sp.Children.Add(sub);
            card.Header = sp;

            string existingCustom = s.MonitorCustomNames.TryGetValue(m.Id, out var custom) ? custom : string.Empty;

            var box = new Wpf.Ui.Controls.TextBox
            {
                Width = 200,
                PlaceholderText = "Ör. Ana Ekran / Çalışma Monitörü",
                Text = existingCustom,
                Tag = m.Id
            };

            _monitorBoxes.Add(box);
            card.Content = box;
            MonitorListPanel.Children.Add(card);
        }
    }

    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not Wpf.Ui.Controls.TextBox box) return;

        var actual = e.Key == Key.System ? e.SystemKey : e.Key;

        // Odak gezintisi (Tab) bozulmasın.
        if (actual == Key.Tab && Keyboard.Modifiers == ModifierKeys.None) return;

        e.Handled = true;

        var gesture = HotkeyGesture.FromKeyEvent(e);
        if (gesture is null)
        {
            // Yalnızca modifier tuşuna (Ctrl, Alt, Shift vb.) basıldıysa sessizce kombinasyonu bekle.
            return;
        }

        HideStatus();
        box.Text = gesture.ToString();
        if ((string?)box.Tag == "Increase") _increase = gesture; else _decrease = gesture;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (_increase == _decrease)
        {
            ShowStatus("Artırma ve azaltma kısayolları farklı olmalıdır.", isError: true);
            return;
        }

        var customNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var box in _monitorBoxes)
        {
            if (box.Tag is string id && !string.IsNullOrWhiteSpace(box.Text))
            {
                customNames[id] = box.Text.Trim();
            }
        }

        var settings = new AppSettings
        {
            StepPercent = (int)Math.Round(StepBox.Value ?? 5),
            RunAtStartup = StartupToggle.IsChecked == true,
            ShowOsd = OsdToggle.IsChecked == true,
            IncreaseHotkey = _increase,
            DecreaseHotkey = _decrease,
            MonitorCustomNames = customNames,
            MonitorBrightness = _current.MonitorBrightness,
            SettingsWindowWidth = (WindowState == WindowState.Normal) ? ActualWidth : _current.SettingsWindowWidth,
            SettingsWindowHeight = (WindowState == WindowState.Normal) ? ActualHeight : _current.SettingsWindowHeight,
        };

        string? error = _apply(settings);
        if (error is null) ShowStatus("Ayarlar kaydedildi.", isError: false);
        else ShowStatus(error, isError: true);
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        Load(new AppSettings
        {
            RunAtStartup = StartupToggle.IsChecked == true,
            MonitorCustomNames = _current.MonitorCustomNames,
            MonitorBrightness = _current.MonitorBrightness,
            SettingsWindowWidth = Width,
            SettingsWindowHeight = Height,
        });
        HideStatus();
    }

    private void ShowStatus(string message, bool isError)
    {
        StatusText.Text = message;
        StatusText.Foreground = (Brush)FindResource(isError ? "SystemFillColorCriticalBrush" : "SystemFillColorSuccessBrush");
        StatusText.Visibility = Visibility.Visible;
    }

    private void HideStatus() => StatusText.Visibility = Visibility.Collapsed;

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            if (IsLoaded) SystemThemeWatcher.UnWatch(this);
        }
        catch { }
        base.OnClosing(e);
    }
}
