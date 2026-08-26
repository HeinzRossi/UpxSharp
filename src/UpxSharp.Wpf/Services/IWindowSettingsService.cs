namespace UpxSharp.Wpf.Services;

public sealed record WindowSettings(
    double Left,
    double Top,
    double Width,
    double Height,
    bool IsMaximized);

public interface IWindowSettingsService
{
    WindowSettings? Load();
    void Save(WindowSettings settings);
}
