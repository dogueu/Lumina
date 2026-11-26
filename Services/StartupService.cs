using Microsoft.Win32;

namespace Lumina.Services;

/// <summary>
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> altında "Windows ile başlat" kaydını yönetir.
/// </summary>
internal sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Lumina";

    private static string ExpectedCommand => $"\"{Environment.ProcessPath}\"";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
    }

    /// <summary>Kayıtlı yol bu .exe'nin yolundan farklıysa false döner.</summary>
    public bool IsPathCurrent
    {
        get
        {
            if (string.IsNullOrEmpty(Environment.ProcessPath)) return false;

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value
                   && string.Equals(value, ExpectedCommand, StringComparison.OrdinalIgnoreCase);
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                        ?? throw new InvalidOperationException("Run kayıt anahtarı açılamadı.");

        if (enabled)
        {
            if (string.IsNullOrEmpty(Environment.ProcessPath))
                throw new InvalidOperationException("Uygulama yolu belirlenemedi.");

            key.SetValue(ValueName, ExpectedCommand, RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
