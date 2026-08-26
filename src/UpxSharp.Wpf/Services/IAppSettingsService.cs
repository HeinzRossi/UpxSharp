namespace UpxSharp.Wpf.Services;

public sealed record AppSettings(bool CloseAfterContextMenuExecution);

public interface IAppSettingsService
{
    AppSettings Load();
    void Save(AppSettings settings);
}
