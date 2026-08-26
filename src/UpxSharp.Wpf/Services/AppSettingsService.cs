using System.IO;
using System.Text.Json;

namespace UpxSharp.Wpf.Services;

public sealed class AppSettingsService : IAppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _settingsPath;

    public AppSettingsService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UpxSharp",
            "settings.json"))
    {
    }

    public AppSettingsService(string settingsPath)
    {
        _settingsPath = settingsPath;
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return new AppSettings(false);

            AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(
                File.ReadAllText(_settingsPath),
                JsonOptions);

            return settings ?? new AppSettings(false);
        }
        catch (JsonException)
        {
            return new AppSettings(false);
        }
        catch (IOException)
        {
            return new AppSettings(false);
        }
        catch (UnauthorizedAccessException)
        {
            return new AppSettings(false);
        }
    }

    public void Save(AppSettings settings)
    {
        string directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);

        string temporaryPath = _settingsPath + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporaryPath, _settingsPath, overwrite: true);
    }
}
