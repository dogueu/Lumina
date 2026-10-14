using System.Windows.Threading;
using Lumina.Native;

namespace Lumina.Services;

/// <summary>
/// Arka plandaki bellek ayak izini küçük tutar: etkinlik bittikten sonra (gecikmeli, tek seferlik)
/// GC'yi sıkıştırır ve çalışma kümesini (working set) işletim sistemine geri verir.
/// </summary>
internal static class MemoryService
{
    private static DispatcherTimer? _timer;

    public static void ScheduleTrim(int delaySeconds = 6)
    {
        _timer ??= CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(delaySeconds);
        _timer.Stop();
        _timer.Start();
    }

    private static DispatcherTimer CreateTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle);
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Trim();
        };
        return timer;
    }

    private static void Trim()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        Kernel32.SetProcessWorkingSetSize(Kernel32.GetCurrentProcess(), (IntPtr)(-1), (IntPtr)(-1));
    }
}
