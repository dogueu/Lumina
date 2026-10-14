using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lumina.Native;
using Microsoft.Win32;

namespace Lumina.Services;

/// <summary>Güneş ikonunu kod ile çizer (DPI ve tema uyumlu, pürüzsüz 32-bit ARGB DIB üretir).</summary>
internal static class IconFactory
{
    public static BitmapSource RenderSun(int size, Color color)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            double s = size;
            double cx = s / 2.0;
            double cy = s / 2.0;

            // Kullanıcı tarafından sağlanan minimalist güneş geometrisi:
            // Merkez daire yarıçapı: 106 / 512
            // Işın kalınlığı: 50 / 512
            // Işın başlangıç merkezi: 182 / 512
            // Işın bitiş merkezi: 230 / 512
            double sunR = s * (106.0 / 512.0);
            double innerR = s * (182.0 / 512.0);
            double outerR = s * (230.0 / 512.0);
            double rayThickness = Math.Max(1.4, s * (50.0 / 512.0));

            var brush = new SolidColorBrush(color);
            brush.Freeze();
            var pen = new Pen(brush, rayThickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            pen.Freeze();

            // Merkez daire
            dc.DrawEllipse(brush, null, new Point(cx, cy), sunR, sunR);

            // 8 adet yuvarlatılmış kapsül ışın
            for (int i = 0; i < 8; i++)
            {
                double angle = i * Math.PI / 4.0;
                double cos = Math.Cos(angle);
                double sin = Math.Sin(angle);
                dc.DrawLine(pen,
                    new Point(cx + innerR * cos, cy + innerR * sin),
                    new Point(cx + outerR * cos, cy + outerR * sin));
            }
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        RenderOptions.SetBitmapScalingMode(bitmap, BitmapScalingMode.HighQuality);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Görev çubuğu temasına göre (açık/koyu) pürüzsüz tepsi HICON'u üretir. Çağıran DestroyIcon ile serbest bırakır.</summary>
    public static IntPtr CreateTrayHIcon()
    {
        // 1080p ve yüksek DPI ekranlarda piksellenmeyi önlemek için en az 32x32 gerçek 32bpp ARGB DIB üretilir
        int size = Math.Max(32, User32.GetSystemMetrics(User32.SM_CXSMICON) * 2);
        var color = IsSystemLightTheme() ? Color.FromRgb(0x1F, 0x1F, 0x1F) : Colors.White;
        return CreateHIcon(RenderSun(size, color));
    }

    public static bool IsSystemLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int v && v == 1;
        }
        catch
        {
            return false;
        }
    }

    private static IntPtr CreateHIcon(BitmapSource bitmap)
    {
        int w = bitmap.PixelWidth;
        int h = bitmap.PixelHeight;
        int stride = w * 4;
        byte[] pixels = new byte[h * stride];
        bitmap.CopyPixels(pixels, stride, 0);

        // Windows Shell'in pürüzsüz saydamlık (anti-aliasing) ile çizmesi için 32-bit DIBSection şarttır
        var bmi = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = w,
            biHeight = -h, // Üstten alta (top-down) DIB
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0 // BI_RGB
        };

        IntPtr hBitmap = Gdi32.CreateDIBSection(IntPtr.Zero, ref bmi, 0, out IntPtr ppvBits, IntPtr.Zero, 0);
        if (hBitmap == IntPtr.Zero) return IntPtr.Zero;

        Marshal.Copy(pixels, 0, ppvBits, pixels.Length);

        // 1bpp boş maske
        int maskStride = ((w + 31) / 32) * 4;
        byte[] maskBits = new byte[maskStride * h];
        IntPtr hMask = Gdi32.CreateBitmap(w, h, 1, 1, maskBits);

        try
        {
            var info = new ICONINFO
            {
                fIcon = true,
                xHotspot = 0,
                yHotspot = 0,
                hbmColor = hBitmap,
                hbmMask = hMask
            };
            return User32.CreateIconIndirect(ref info);
        }
        finally
        {
            Gdi32.DeleteObject(hBitmap);
            Gdi32.DeleteObject(hMask);
        }
    }
}
