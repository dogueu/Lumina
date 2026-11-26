using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Lumina.Services;

/// <summary>
/// Hafif tanı ve beklenmeyen hata kayıt yardımcısı.
/// Debug izleme mesajları yalnızca DEBUG derlemesinde üretilir.
/// </summary>
internal static class AppDiagnostics
{
    private static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Lumina",
        "last-crash.log");

    [Conditional("DEBUG")]
    public static void Trace(string message) => Debug.WriteLine($"[Lumina] {message}");

    public static bool IsFatal(Exception ex) => ex is
        OutOfMemoryException or
        StackOverflowException or
        AccessViolationException or
        SEHException;

    public static void RecordCrash(string source, Exception? ex)
    {
        if (ex is null) return;

        Trace($"{source}: {ex}");
        try
        {
            string? dir = Path.GetDirectoryName(CrashLogPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            string content = $"[{DateTimeOffset.Now:O}] ({source}){Environment.NewLine}{ex}{Environment.NewLine}";
            File.WriteAllText(CrashLogPath, content);
        }
        catch
        {
            // Hata günlüğü yazılamazsa ikincil çökme oluşturma.
        }
    }
}
