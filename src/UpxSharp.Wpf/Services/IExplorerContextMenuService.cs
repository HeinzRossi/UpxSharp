namespace UpxSharp.Wpf.Services;

public enum ExplorerIntegrationStatus
{
    NotInstalled,
    ClassicInstalled,
    ShellExtensionInstalled,
}

public interface IExplorerContextMenuService
{
    string LastInstallMessage { get; }
    void InstallForCurrentUser(bool autoPack);
    void RemoveForCurrentUser();
    bool IsInstalledForCurrentUser();
    ExplorerIntegrationStatus GetStatus();
}
