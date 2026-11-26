using System.Windows.Threading;
using Lumina.Native;

namespace Lumina.Services;

/// <summary>
/// Etkinlik bittikten sonra (gecikmeli, tek seferlik) arka planda belleği sıkıştırır ve
/// çalışma kümesini işletim sistemine geri verir.
/// </summary>
internal static class MemoryService
{
    private static DispatcherTimer? _timer;
    private static int _trimming;

    public static void ScheduleTrim(int delaySeconds = 6)
    {
        _timer ??= CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(delaySeconds);
        _timer.Stop();
        _timer.Start();
    }

    public static void Stop() => _timer?.Stop();

    private static DispatcherTimer CreateTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle);
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            TriggerTrimAsync();
        };
        return timer;
    }

    private static void TriggerTrimAsync()
    {
        if (Interlocked.Exchange(ref _trimming, 1) != 0) return;

        _ = Task.Run(() =>
        {
            try
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                Kernel32.SetProcessWorkingSetSize(Kernel32.GetCurrentProcess(), (IntPtr)(-1), (IntPtr)(-1));
            }
            catch (Exception ex)
            {
                AppDiagnostics.Trace($"Bellek sıkıştırma hatası: {ex.Message}");
            }
            finally
            {
                Volatile.Write(ref _trimming, 0);
            }
        });
    }
}
