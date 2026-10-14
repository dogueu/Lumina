using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Lumina.Native;
using Lumina.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Lumina.Views;

/// <summary>
/// Windows 11 ses göstergesine benzer, şeffaf, tıklanamayan ve odak almayan geçici parlaklık göstergesi.
/// </summary>
public partial class OsdWindow : Window
{
    private static readonly TimeSpan VisibleFor = TimeSpan.FromMilliseconds(1600);

    private readonly DispatcherTimer _hideTimer = new() { Interval = VisibleFor };
    private bool _fadingOut;

    public OsdWindow()
    {
        InitializeComponent();
        Background = Brushes.Transparent;

        if (Application.Current != null && ReferenceEquals(Application.Current.MainWindow, this))
        {
            Application.Current.MainWindow = null;
        }

        SourceInitialized += (_, _) =>
        {
            MakeClickThrough();
            WindowBackdrop.RemoveBackdrop(this);
            WindowBackdrop.RemoveBackground(this);
        };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            FadeOut();
        };
    }

    public void ShowLevel(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        ApplyTheme();

        Level.Value = percent;
        PercentText.Text = percent.ToString();
        SunIcon.Symbol = percent switch
        {
            < 34 => SymbolRegular.BrightnessLow24,
            < 67 => SymbolRegular.WeatherSunny24,
            _ => SymbolRegular.BrightnessHigh24,
        };

        bool isAlreadyShowing = IsVisible && !_fadingOut && Opacity > 0.05;

        if (_fadingOut)
        {
            _fadingOut = false;
            CardTransform.BeginAnimation(TranslateTransform.YProperty, null);
            BeginAnimation(OpacityProperty, null);
        }

        if (!IsVisible)
        {
            Opacity = 0;
            CardTransform.Y = 6;
            Show();
            UpdateLayout();
            Reposition();
        }

        if (!isAlreadyShowing)
        {
            // Sadece ilk açılışta alttan hafif yükselme ve belirme (Windows 11 ile birebir)
            CardTransform.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(160))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });

            BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, TimeSpan.FromMilliseconds(140))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
        }
        else
        {
            // Zaten ekranda görünürken değer değişiyorsa ASLA yeniden zıplama
            CardTransform.Y = 0;
            Opacity = 1;
        }

        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void FadeOut()
    {
        _fadingOut = true;
        var duration = TimeSpan.FromMilliseconds(180);
        var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };

        var fade = new DoubleAnimation(0, duration) { EasingFunction = easeIn };
        var slide = new DoubleAnimation(CardTransform.Y, 6, duration) { EasingFunction = easeIn };

        fade.Completed += (_, _) =>
        {
            if (!_fadingOut) return;
            _fadingOut = false;
            BeginAnimation(OpacityProperty, null);
            CardTransform.BeginAnimation(TranslateTransform.YProperty, null);
            Hide();
            MemoryService.ScheduleTrim();
        };

        CardTransform.BeginAnimation(TranslateTransform.YProperty, slide);
        BeginAnimation(OpacityProperty, fade);
    }

    private const double CardWidthDip = 192;
    private const double CardHeightDip = 46;
    private const double BottomGapDip = 14;

    /// <summary>Birincil monitörde, görev çubuğunun hemen üzerinde alt-orta konum (Windows 11 ses OSD'si ile birebir).</summary>
    private void Reposition()
    {
        var area = ScreenHelper.GetMonitorAt(0, 0, primary: true);

        int cardCenterXPx = area.Work.Left + area.Work.Width / 2;
        int x = cardCenterXPx - (int)Math.Round((CardWidthDip / 2) * area.Scale);

        int cardBottomYPx = area.Work.Bottom - (int)Math.Round(BottomGapDip * area.Scale);
        int y = cardBottomYPx - (int)Math.Round(CardHeightDip * area.Scale);

        double scale = area.Scale > 0 ? area.Scale : 1.0;
        Left = x / scale;
        Top = y / scale;

        ScreenHelper.MoveWindow(this, x, y);
    }

    private void ApplyTheme()
    {
        bool dark = ApplicationThemeManager.GetAppTheme() != ApplicationTheme.Light;
        var background = dark ? Color.FromRgb(0x2C, 0x2C, 0x2E) : Color.FromRgb(0xF3, 0xF3, 0xF3);
        var foreground = dark ? Colors.White : Color.FromRgb(0x1A, 0x1A, 0x1A);
        var trackColor = dark ? Color.FromRgb(0x9E, 0x9E, 0x9E) : Color.FromRgb(0xC0, 0xC0, 0xC0);

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

        Card.Background = new SolidColorBrush(background);
        Card.BorderBrush = Brushes.Transparent;
        Card.BorderThickness = new Thickness(0);
        Background = Brushes.Transparent;
        WindowBackdrop.RemoveBackdrop(this);
        WindowBackdrop.RemoveBackground(this);
        var fg = new SolidColorBrush(foreground);
        SunIcon.Foreground = fg;
        PercentText.Foreground = fg;

        Level.Background = new SolidColorBrush(trackColor);
        Level.Foreground = accentBrush;
    }

    private void MakeClickThrough()
    {
        var handle = new WindowInteropHelper(this).Handle;
        int style = User32.GetWindowLong(handle, User32.GWL_EXSTYLE);
        User32.SetWindowLong(handle, User32.GWL_EXSTYLE,
            style | User32.WS_EX_TRANSPARENT | User32.WS_EX_NOACTIVATE | User32.WS_EX_TOOLWINDOW);
    }
}
