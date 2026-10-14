using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Lumina.Native;

namespace Lumina.Services;

internal readonly record struct MonitorArea(RECT Monitor, RECT Work, double Scale);

/// <summary>Piksel tabanlı ekran/monitör yardımcıları (PerMonitorV2).</summary>
internal static class ScreenHelper
{
    public static MonitorArea GetMonitorAt(int x, int y, bool primary = false)
    {
        var hMon = User32.MonitorFromPoint(new POINT { X = x, Y = y },
            primary ? User32.MONITOR_DEFAULTTOPRIMARY : User32.MONITOR_DEFAULTTONEAREST);

        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (!User32.GetMonitorInfo(hMon, ref info))
        {
            int w = (int)SystemParameters.PrimaryScreenWidth, h = (int)SystemParameters.PrimaryScreenHeight;
            var rect = new RECT { Left = 0, Top = 0, Right = w, Bottom = h };
            return new MonitorArea(rect, rect, 1.0);
        }

        double scale = 1.0;
        try
        {
            if (Shcore.GetDpiForMonitor(hMon, Shcore.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0)
                scale = dpiX / 96.0;
        }
        catch
        {
            // eski Windows sürümlerinde shcore yoksa 1.0 kalır
        }

        return new MonitorArea(info.rcMonitor, info.rcWork, scale);
    }

    public static void MoveWindow(Window window, int xPx, int yPx)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        User32.SetWindowPos(handle, IntPtr.Zero, xPx, yPx, 0, 0,
            User32.SWP_NOSIZE | User32.SWP_NOZORDER | User32.SWP_NOACTIVATE);
    }

    /// <summary>Pencerenin şu anki (DPI'ya göre) piksel boyutunu verir.</summary>
    public static (int Width, int Height) GetPixelSize(Window window)
    {
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
        return ((int)Math.Round(window.ActualWidth * dpi.DpiScaleX),
                (int)Math.Round(window.ActualHeight * dpi.DpiScaleY));
    }
}
