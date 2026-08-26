using System.IO;
using System.Text.Json;
using System.Windows;

namespace UpxSharp.Wpf.Services;

public sealed class WindowSettingsService : IWindowSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _settingsPath;

    public WindowSettingsService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UpxSharp",
            "window-settings.json"))
    {
    }

    public WindowSettingsService(string settingsPath)
    {
        _settingsPath = settingsPath;
    }

    public WindowSettings? Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return null;

            var settings = JsonSerializer.Deserialize<WindowSettings>(
                File.ReadAllText(_settingsPath),
                JsonOptions);

            return IsValid(settings) ? settings : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(WindowSettings settings)
    {
        if (!IsValid(settings))
            throw new ArgumentException("Configurações de janela inválidas.", nameof(settings));

        string directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);

        string temporaryPath = _settingsPath + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporaryPath, _settingsPath, overwrite: true);
    }

    public static WindowSettings Normalize(
        WindowSettings settings,
        Rect workArea,
        Rect virtualScreen,
        double minWidth = 800,
        double minHeight = 500)
    {
        double width = Math.Clamp(settings.Width, minWidth, workArea.Width);
        double height = Math.Clamp(settings.Height, minHeight, workArea.Height);

        bool isVisible = settings.Left + 100 <= virtualScreen.Right
                         && settings.Top + 100 <= virtualScreen.Bottom
                         && settings.Left + width - 100 >= virtualScreen.Left
                         && settings.Top + height - 100 >= virtualScreen.Top;

        if (isVisible)
            return settings with { Width = width, Height = height };

        return settings with
        {
            Left = workArea.Left + (workArea.Width - width) / 2,
            Top = workArea.Top + (workArea.Height - height) / 2,
            Width = width,
            Height = height,
        };
    }

    private static bool IsValid(WindowSettings? settings)
        => settings is not null
           && double.IsFinite(settings.Left)
           && double.IsFinite(settings.Top)
           && double.IsFinite(settings.Width)
           && double.IsFinite(settings.Height)
           && settings.Width >= 800
           && settings.Height >= 500;
}
