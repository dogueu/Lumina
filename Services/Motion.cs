using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Lumina.Services;

/// <summary>
/// Arayüz hareket sistemi: QuinticEase çıkış / CubicEase giriş eğrileri, yumuşak hover geçişi,
/// basma ölçeği, kayan 40x20 toggle anahtarı ve görünümler arası yatay geçiş.
/// </summary>
public static class Motion
{
    public static readonly IEasingFunction Out = Frozen(new QuinticEase { EasingMode = EasingMode.EaseOut });
    public static readonly IEasingFunction In = Frozen(new CubicEase { EasingMode = EasingMode.EaseIn });

    private static IEasingFunction Frozen(EasingFunctionBase e) { e.Freeze(); return e; }

    public static bool Enabled => SystemParameters.ClientAreaAnimation;

    public static Duration Ms(double ms) => new(TimeSpan.FromMilliseconds(Enabled ? ms : 1));

    // ---------------- placeholder ----------------

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached(
        "Placeholder", typeof(string), typeof(Motion), new PropertyMetadata(string.Empty));

    public static string GetPlaceholder(DependencyObject d) => (string)d.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject d, string v) => d.SetValue(PlaceholderProperty, v);

    // ---------------- hover ----------------

    public static readonly DependencyProperty HoverProperty = DependencyProperty.RegisterAttached(
        "Hover", typeof(Brush), typeof(Motion), new PropertyMetadata(null, OnHoverChanged));

    public static Brush? GetHover(DependencyObject d) => (Brush?)d.GetValue(HoverProperty);
    public static void SetHover(DependencyObject d, Brush? v) => d.SetValue(HoverProperty, v);

    /// <summary>Tıklama ölçeği (0.97 buton, 0.985 kart satırı; 0 = kapalı).</summary>
    public static readonly DependencyProperty PressProperty = DependencyProperty.RegisterAttached(
        "Press", typeof(double), typeof(Motion), new PropertyMetadata(0.0, OnPressChanged));

    public static double GetPress(DependencyObject d) => (double)d.GetValue(PressProperty);
    public static void SetPress(DependencyObject d, double v) => d.SetValue(PressProperty, v);

    private static readonly DependencyProperty HostedProperty = DependencyProperty.RegisterAttached(
        "Hosted", typeof(bool), typeof(Motion));

    private static void OnHoverChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Border b || e.OldValue is not null) return;
        b.Loaded += (_, _) =>
        {
            var host = (b.TemplatedParent as UIElement) ?? b;
            if (host.GetValue(HostedProperty) is true) return;
            host.SetValue(HostedProperty, true);
            host.MouseEnter += (_, _) => Fade(b, true);
            host.MouseLeave += (_, _) => Fade(b, false);
        };
    }

    private static void Fade(Border b, bool on)
    {
        if (GetHover(b) is not SolidColorBrush hover) return;
        var current = (b.Background as SolidColorBrush)?.Color ?? Colors.Transparent;
        var rest = RestColor(b, hover.Color);
        var target = on ? hover.Color : rest;
        if (current.A == 0) current = Color.FromArgb(0, target.R, target.G, target.B);

        var brush = new SolidColorBrush(current);
        b.Background = brush;
        var anim = new ColorAnimation(target, Ms(on ? 150 : 200));
        if (!on)
        {
            anim.Completed += (_, _) =>
            {
                if (!b.IsMouseOver && !(b.TemplatedParent is UIElement p && p.IsMouseOver))
                    b.ClearValue(Border.BackgroundProperty);
            };
        }
        brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
    }

    private static Color RestColor(Border b, Color hover)
    {
        var local = b.ReadLocalValue(Border.BackgroundProperty);
        b.ClearValue(Border.BackgroundProperty);
        var rest = (b.Background as SolidColorBrush)?.Color ?? Color.FromArgb(0, hover.R, hover.G, hover.B);
        if (local != DependencyProperty.UnsetValue) b.SetValue(Border.BackgroundProperty, local);
        return rest.A == 0 ? Color.FromArgb(0, hover.R, hover.G, hover.B) : rest;
    }

    private static void OnPressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement el || (double)e.OldValue != 0) return;
        var scale = new ScaleTransform(1, 1);
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        el.RenderTransform = scale;

        void To(double v, double ms)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(v, Ms(ms)) { EasingFunction = Out });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(v, Ms(ms)) { EasingFunction = Out });
        }

        el.PreviewMouseLeftButtonDown += (_, _) => To(GetPress(el), 90);
        el.PreviewMouseLeftButtonUp += (_, _) => To(1, 220);
        el.MouseLeave += (_, _) => To(1, 220);
    }

    // ---------------- switch ----------------

    public static readonly DependencyProperty SwitchProperty = DependencyProperty.RegisterAttached(
        "Switch", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnSwitchChanged));

    public static bool GetSwitch(DependencyObject d) => (bool)d.GetValue(SwitchProperty);
    public static void SetSwitch(DependencyObject d, bool v) => d.SetValue(SwitchProperty, v);

    private static void OnSwitchChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ToggleButton tb || e.NewValue is not true) return;
        const double travel = 20;

        Ellipse? Knob() => tb.Template?.FindName("Knob", tb) as Ellipse;

        static TranslateTransform Move(Ellipse k)
        {
            if (k.RenderTransform is TranslateTransform t) return t;
            t = new TranslateTransform();
            k.RenderTransform = t;
            return t;
        }

        void Place(bool animate)
        {
            if (Knob() is not { } k) return;
            var t = Move(k);
            double to = tb.IsChecked == true ? travel : 0;
            if (animate) Run(t, TranslateTransform.XProperty, null, to, 260, Out);
            else
            {
                t.BeginAnimation(TranslateTransform.XProperty, null);
                t.X = to;
            }
            Run(k, FrameworkElement.WidthProperty, null, 12, 160, Out);
        }

        tb.Loaded += (_, _) => Place(false);
        tb.Checked += (_, _) => Place(true);
        tb.Unchecked += (_, _) => Place(true);
        tb.PreviewMouseLeftButtonDown += (_, _) =>
        {
            if (Knob() is not { } k) return;
            Run(k, FrameworkElement.WidthProperty, null, 15, 120, Out);
            if (tb.IsChecked == true) Run(Move(k), TranslateTransform.XProperty, null, travel - 3, 120, Out);
        };
        tb.MouseLeave += (_, _) => Place(true);
    }

    // ---------------- helpers ----------------

    public static void Run(IAnimatable target, DependencyProperty p, double? from, double to, double ms, IEasingFunction ease, Action? done = null)
    {
        var a = new DoubleAnimation(to, Ms(ms))
        {
            EasingFunction = ease,
        };
        if (from is double f) a.From = f;
        if (done is not null) a.Completed += (_, _) => done();
        target.BeginAnimation(p, a);
    }

    /// <summary>Görünümler arası yatay kaymalı geçiş (dir: +1 ileri, -1 geri).</summary>
    public static void EnterView(UIElement view, int dir)
    {
        var t = new TranslateTransform(28 * dir, 0);
        view.RenderTransform = t;
        Run(view, UIElement.OpacityProperty, 0, 1, 300, Out);
        Run(t, TranslateTransform.XProperty, 28 * dir, 0, 420, Out);
    }
}
