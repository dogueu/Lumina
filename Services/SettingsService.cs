using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using Lumina.Models;

namespace Lumina.Services;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, int>))]
[JsonSerializable(typeof(HashSet<string>))]
[JsonSerializable(typeof(List<string>))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;

/// <summary>Ayarları %AppData%\Lumina\appsettings.json dosyasında saklar.</summary>
internal sealed class SettingsService
{
    private const int SaveDebounceMs = 250;

    private readonly string _directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lumina");

    private DispatcherTimer? _saveTimer;
    private bool _dirty;

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
            AppDiagnostics.Trace($"Ayarlar okunamadı, varsayılanlar kullanılacak: {ex.Message}");
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
            AppDiagnostics.Trace($"Ayarlar kaydedilemedi: {ex.Message}");
            return false;
        }
    }

    /// <summary>Parlaklık değişimleri gibi sık tetiklenen ayarları 250 ms debounce ile diske kaydeder.</summary>
    public void ScheduleSave()
    {
        _dirty = true;
        if (_saveTimer is null)
        {
            _saveTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(SaveDebounceMs)
            };
            _saveTimer.Tick += (_, _) =>
            {
                _saveTimer.Stop();
                _dirty = false;
                Save(Current);
            };
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Uygulama kapanırken veya oturum sonlanırken bekleyen değişiklikleri anında diske yazar.</summary>
    public void Flush()
    {
        if (_saveTimer is not null)
            _saveTimer.Stop();

        if (_dirty)
        {
            _dirty = false;
            Save(Current);
        }
    }
}
