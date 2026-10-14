using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lumina.Models;

namespace Lumina.Services;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, int>))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;

/// <summary>Ayarları %AppData%\Lumina\appsettings.json dosyasında saklar.</summary>
internal sealed class SettingsService
{
    private readonly string _directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lumina");

    private string FilePath => Path.Combine(_directory, "appsettings.json");

    public AppSettings Current { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                using var stream = File.OpenRead(FilePath);
                var loaded = JsonSerializer.Deserialize(stream, SettingsJsonContext.Default.AppSettings);
                if (loaded is not null)
                {
                    loaded.Normalize();
                    Current = loaded;
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Lumina] Ayarlar okunamadı, varsayılanlar kullanılacak: {ex.Message}");
        }

        Current = new AppSettings();
    }

    public bool Save(AppSettings settings)
    {
        try
        {
            settings.Normalize();
            Directory.CreateDirectory(_directory);

            // Atomik yazım: yarım kalmış dosya riskini önler.
            string temp = FilePath + ".tmp";
            using (var stream = File.Create(temp))
            {
                JsonSerializer.Serialize(stream, settings, SettingsJsonContext.Default.AppSettings);
            }
            File.Move(temp, FilePath, overwrite: true);

            Current = settings;
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Lumina] Ayarlar kaydedilemedi: {ex.Message}");
            return false;
        }
    }

    private System.Windows.Threading.DispatcherTimer? _saveTimer;

    /// <summary>Parlaklık değişimleri gibi sık tetiklenen ayarları 1 saniye debounce ile diske kaydeder.</summary>
    public void ScheduleSave()
    {
        if (_saveTimer is null)
        {
            _saveTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _saveTimer.Tick += (_, _) =>
            {
                _saveTimer.Stop();
                Save(Current);
            };
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }
}
