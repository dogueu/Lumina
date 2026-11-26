using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Lumina.Services;

namespace Lumina.Views;

/// <summary>
/// Şeffaf, tıklanamayan ve odak almayan geçici parlaklık göstergesi (OSD).
/// </summary>
public partial class OsdWindow : Window
{
    private const double CardWidthDip = 192;
    private const double CardHeightDip = 46;
    private const double BottomGapDip = 14;
    private static readonly TimeSpan VisibleFor = TimeSpan.FromMilliseconds(1600);

    private readonly DispatcherTimer _hideTimer = new() { Interval = VisibleFor };
    private BlurUnderlay? _blur;
    private bool _fadingOut;
    private int _animGen;
    private double _currentScale = 1.0;

    public OsdWindow()
    {
        InitializeComponent();

        if (Application.Current is not null && ReferenceEquals(Application.Current.MainWindow, this))
            Application.Current.MainWindow = null;

        SourceInitialized += (_, _) =>
        {
            ThemeService.MakeToolWindow(this, noActivate: true, clickThrough: true);
            _blur = BlurUnderlay.Attach(this);
            UpdateBlur();
        };

        ThemeService.Changed += OnThemeChanged;
        Closed += (_, _) =>
        {
            _hideTimer.Stop();
            ThemeService.Changed -= OnThemeChanged;
        };

        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            FadeOut();
        };
    }

    private void OnThemeChanged() => Dispatcher.BeginInvoke(UpdateBlur);

    public void ShowLevel(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);

        FillCol.Width = new GridLength(percent, GridUnitType.Star);
        RestCol.Width = new GridLength(100 - percent, GridUnitType.Star);
        PercentText.Text = percent.ToString();

        bool useBlur = ThemeService.Blur;
        bool isAlreadyShowing = IsVisible && !_fadingOut && (useBlur ? CardBody.Opacity > 0.05 : Opacity > 0.05);

        _animGen++;
        if (_fadingOut)
        {
            _fadingOut = false;
            BodyTransform.BeginAnimation(TranslateTransform.YProperty, null);
            CardBody.BeginAnimation(OpacityProperty, null);
            BeginAnimation(OpacityProperty, null);
        }

        if (!IsVisible)
        {
            BodyTransform.Y = 6;
            if (useBlur)
            {
                Opacity = 1;
                CardBody.Opacity = 0;
            }
            else
            {
                CardBody.Opacity = 1;
                Opacity = 0;
            }
            Show();
            UpdateLayout();
            Reposition();
        }

        UpdateBlur();

        if (!isAlreadyShowing)
        {
            Motion.Run(BodyTransform, TranslateTransform.YProperty, 6, 0, 180, Motion.Out);
            if (useBlur)
            {
                Opacity = 1;
                Motion.Run(CardBody, OpacityProperty, 0, 1, 150, Motion.Out);
            }
            else
            {
                CardBody.Opacity = 1;
                Motion.Run(this, OpacityProperty, 0, 1, 150, Motion.Out);
            }
        }
        else
        {
            BodyTransform.Y = 0;
            Opacity = 1;
            CardBody.Opacity = 1;
        }

        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void FadeOut()
    {
        _fadingOut = true;
        int gen = ++_animGen;

        if (!Motion.Enabled)
        {
            _fadingOut = false;
            Hide();
            MemoryService.ScheduleTrim();
            return;
        }

        void FinishFade()
        {
            if (!_fadingOut || gen != _animGen) return;
            _fadingOut = false;
            BeginAnimation(OpacityProperty, null);
            CardBody.BeginAnimation(OpacityProperty, null);
            BodyTransform.BeginAnimation(TranslateTransform.YProperty, null);
            Hide();
            MemoryService.ScheduleTrim();
        }

        Motion.Run(BodyTransform, TranslateTransform.YProperty, null, 6, 160, Motion.In);
        if (ThemeService.Blur)
            Motion.Run(CardBody, OpacityProperty, null, 0, 160, Motion.In, FinishFade);
        else
            Motion.Run(this, OpacityProperty, null, 0, 160, Motion.In, FinishFade);
    }

    private void UpdateBlur()
    {
        double opacity = IsVisible ? ThemeService.BlurOpacity : 0;
        _blur?.Update(8 * _currentScale, opacity);
    }

    /// <summary>Birincil monitörde, görev çubuğunun hemen üzerinde alt-orta konum.</summary>
    private void Reposition()
    {
        var area = ScreenHelper.GetMonitorAt(0, 0, primary: true);
        _currentScale = area.Scale > 0 ? area.Scale : 1.0;

        int cardCenterXPx = area.Work.Left + area.Work.Width / 2;
        int x = cardCenterXPx - (int)Math.Round((CardWidthDip / 2) * _currentScale);

        int cardBottomYPx = area.Work.Bottom - (int)Math.Round(BottomGapDip * _currentScale);
        int y = cardBottomYPx - (int)Math.Round(CardHeightDip * _currentScale);

        Left = x / _currentScale;
        Top = y / _currentScale;

        ScreenHelper.MoveWindow(this, x, y);
    }
}
