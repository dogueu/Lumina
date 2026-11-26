using System.Windows.Media;

namespace Lumina.Services;

/// <summary>
/// 20x20 ızgarada çizilmiş, donmuş vektör silüet simgeler.
/// </summary>
public static class Icons
{
    public static readonly Geometry Monitor = F(
        "M4 4h12a2 2 0 0 1 2 2v7.5a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2z" +
        "M4.2 6h11.6v7.5H4.2z",
        "M10 15.5V17.5M7 17.5h6");

    public static readonly Geometry Sun = F(
        Dot(10, 10, 3.2),
        "M10 2.6v1.8M10 15.6v1.8M2.6 10h1.8M15.6 10h1.8M4.8 4.8l1.3 1.3M13.9 13.9l1.3 1.3M15.2 4.8l-1.3 1.3M6.1 13.9l-1.3 1.3");

    public static readonly Geometry Refresh = F(null, "M16.25 10a6.25 6.25 0 1 1-1.83-4.42M14.5 2.75v3h-3");
    public static readonly Geometry Spinner = F(null, "M16 10a6 6 0 1 1-6-6");
    public static readonly Geometry Sliders = F(Cap(4.5, 4.5, 6, 4) + Cap(9.5, 11.5, 6, 4), "M3 6.5h14M3 13.5h14");
    public static readonly Geometry Back = F(null, "M12.5 4.5 7 10l5.5 5.5");
    public static readonly Geometry Dismiss = F(null, "M5.5 5.5l9 9M14.5 5.5l-9 9");
    public static readonly Geometry Grip = F(
        Dot(7.5, 5.2, 1.3) + Dot(12.5, 5.2, 1.3) +
        Dot(7.5, 10.0, 1.3) + Dot(12.5, 10.0, 1.3) +
        Dot(7.5, 14.8, 1.3) + Dot(12.5, 14.8, 1.3));

    private static Geometry F(string? fill, string? stroke = null, double strokeWidth = 1.8)
    {
        Geometry? g = fill is null ? null : Geometry.Parse("F0 " + fill);
        if (stroke is not null)
        {
            var pen = new Pen(Brushes.Black, strokeWidth)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
            };
            Geometry wide = Geometry.Parse(stroke).GetWidenedPathGeometry(pen);
            g = g is null ? wide : Geometry.Combine(g, wide, GeometryCombineMode.Union, null);
        }

        g!.Freeze();
        return g;
    }

    private static string Cap(double x, double y, double w, double h)
    {
        double r = Math.Min(w, h) / 2;
        return w >= h
            ? FormattableString.Invariant($"M{x + r} {y}h{w - 2 * r}a{r} {r} 0 0 1 0 {h}h{-(w - 2 * r)}a{r} {r} 0 0 1 0 {-h}z")
            : FormattableString.Invariant($"M{x} {y + r}a{r} {r} 0 0 1 {w} 0v{h - 2 * r}a{r} {r} 0 0 1 {-w} 0z");
    }

    private static string Dot(double x, double y, double r) =>
        FormattableString.Invariant($"M{x} {y - r}a{r} {r} 0 1 0 0 {2 * r}a{r} {r} 0 1 0 0 {-2 * r}z");
}
