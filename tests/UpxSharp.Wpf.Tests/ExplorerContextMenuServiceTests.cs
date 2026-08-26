using Microsoft.Win32;
using UpxSharp.Wpf.Services;
using Xunit;

namespace UpxSharp.Wpf.Tests;

public sealed class ExplorerContextMenuServiceTests : IDisposable
{
    private readonly string _baseKeyPath = $@"Software\UpxSharp.Tests\{Guid.NewGuid():N}";
    private readonly string _menuKeyPath;
    private readonly string _wildcardMenuKeyPath;
    private readonly string _problematicMenuKeyPath;
    private readonly string _legacyMenuKeyPath;
    private readonly string _shellExtensionClsidKeyPath;

    public ExplorerContextMenuServiceTests()
    {
        _menuKeyPath = $@"{_baseKeyPath}\SystemFileAssociations\Exe\shell\UpxSharp";
        _wildcardMenuKeyPath = $@"{_baseKeyPath}\AllFiles\shell\UpxSharp";
        _problematicMenuKeyPath = $@"{_baseKeyPath}\ProblematicExefileShell\UpxSharp";
        _legacyMenuKeyPath = $@"{_baseKeyPath}\LegacyExefileShell\CompressWithUpxSharp";
        _shellExtensionClsidKeyPath = $@"{_baseKeyPath}\CLSID\{{7F5D0D52-7266-4B01-96D0-744153A0BB02}}";
    }

    [Fact]
    public void GetStatus_WhenMissing_ReturnsNotInstalled()
    {
        var service = CreateService("C:\\missing\\UpxSharp.Wpf.exe");

        Assert.False(service.IsInstalledForCurrentUser());
        Assert.Equal(ExplorerIntegrationStatus.NotInstalled, service.GetStatus());
    }

    [Fact]
    public void InstallForCurrentUser_WithoutShellExtension_CreatesClassicMenuOutsideExefileShell()
    {
        using var directory = new TempDirectory();
        string executablePath = Path.Combine(directory.Path, "UpxSharp.Wpf.exe");
        File.WriteAllText(executablePath, string.Empty);
        var service = CreateService(executablePath);

        service.InstallForCurrentUser(autoPack: true);

        Assert.True(service.IsInstalledForCurrentUser());
        Assert.Equal(ExplorerIntegrationStatus.ClassicInstalled, service.GetStatus());
        Assert.Contains("Menu clássico instalado", service.LastInstallMessage);
        Assert.Contains("comhost", service.LastInstallMessage);
        using RegistryKey menuKey = Registry.CurrentUser.OpenSubKey(_menuKeyPath)
            ?? throw new InvalidOperationException("Chave do menu não encontrada.");
        using RegistryKey commandKey = Registry.CurrentUser.OpenSubKey($@"{_menuKeyPath}\command")
            ?? throw new InvalidOperationException("Chave do comando não encontrada.");
        using RegistryKey wildcardMenuKey = Registry.CurrentUser.OpenSubKey(_wildcardMenuKeyPath)
            ?? throw new InvalidOperationException("Chave wildcard do menu não encontrada.");
        using RegistryKey wildcardCommandKey = Registry.CurrentUser.OpenSubKey($@"{_wildcardMenuKeyPath}\command")
            ?? throw new InvalidOperationException("Chave wildcard do comando não encontrada.");
        Assert.Equal(string.Empty, menuKey.GetValue(null));
        Assert.Equal("Comprimir com UpxSharp", menuKey.GetValue("MUIVerb"));
        Assert.Equal($"{executablePath},0", menuKey.GetValue("Icon"));
        Assert.Equal("Single", menuKey.GetValue("MultiSelectModel"));
        Assert.Equal("Top", menuKey.GetValue("Position"));
        Assert.Null(menuKey.GetValue("ExplorerCommandHandler"));
        Assert.Equal($"\"{executablePath}\" --context-menu \"%1\"", commandKey.GetValue(null));
        Assert.Equal("System.FileExtension:=\".exe\"", wildcardMenuKey.GetValue("AppliesTo"));
        Assert.Equal("Comprimir com UpxSharp", wildcardMenuKey.GetValue("MUIVerb"));
        Assert.Equal($"\"{executablePath}\" --context-menu \"%1\"", wildcardCommandKey.GetValue(null));
        Assert.Null(Registry.CurrentUser.OpenSubKey(_problematicMenuKeyPath));
    }

    [Fact]
    public void InstallForCurrentUser_WithShellExtension_RegistersClsidButKeepsClassicCommandSimple()
    {
        using var directory = new TempDirectory();
        string executablePath = Path.Combine(directory.Path, "UpxSharp.Wpf.exe");
        string shellExtensionPath = Path.Combine(directory.Path, "UpxSharp.Shell.comhost.dll");
        File.WriteAllText(executablePath, string.Empty);
        File.WriteAllText(shellExtensionPath, string.Empty);
        var service = CreateService(
            executablePath,
            isSparsePackageInstalled: _ => true);

        service.InstallForCurrentUser(autoPack: true);

        Assert.Equal(ExplorerIntegrationStatus.ShellExtensionInstalled, service.GetStatus());
        using RegistryKey menuKey = Registry.CurrentUser.OpenSubKey(_menuKeyPath)
            ?? throw new InvalidOperationException("Chave do menu não encontrada.");
        using RegistryKey clsidKey = Registry.CurrentUser.OpenSubKey(_shellExtensionClsidKeyPath)
            ?? throw new InvalidOperationException("Chave CLSID não encontrada.");
        using RegistryKey inprocKey = Registry.CurrentUser.OpenSubKey($@"{_shellExtensionClsidKeyPath}\InprocServer32")
            ?? throw new InvalidOperationException("Chave InprocServer32 não encontrada.");
        Assert.Null(menuKey.GetValue("ExplorerCommandHandler"));
        Assert.Equal("UpxSharp Shell Extension", clsidKey.GetValue(null));
        Assert.Equal(executablePath, clsidKey.GetValue("AppPath"));
        Assert.Equal($"{executablePath},0", clsidKey.GetValue("IconPath"));
        Assert.Equal(shellExtensionPath, inprocKey.GetValue(null));
        Assert.Equal("Apartment", inprocKey.GetValue("ThreadingModel"));
    }

    [Fact]
    public void InstallForCurrentUser_WithModernFiles_DoesNotRegisterSparsePackageWhileModernIntegrationIsPaused()
    {
        using var directory = new TempDirectory();
        string executablePath = Path.Combine(directory.Path, "UpxSharp.Wpf.exe");
        string shellExtensionPath = Path.Combine(directory.Path, "UpxSharp.Shell.comhost.dll");
        string packagePath = Path.Combine(directory.Path, "UpxSharp.Sparse.msix");
        File.WriteAllText(executablePath, string.Empty);
        File.WriteAllText(shellExtensionPath, string.Empty);
        File.WriteAllText(packagePath, string.Empty);
        bool registerSparsePackageCalled = false;
        bool sparsePackageInstalled = false;
        var service = CreateService(
            executablePath,
            isSparsePackageInstalled: _ => sparsePackageInstalled,
            registerSparsePackage: appDirectory =>
            {
                Assert.Equal(directory.Path, appDirectory);
                registerSparsePackageCalled = true;
                sparsePackageInstalled = true;
            });

        service.InstallForCurrentUser(autoPack: true);

        Assert.False(registerSparsePackageCalled);
        Assert.Equal(ExplorerIntegrationStatus.ClassicInstalled, service.GetStatus());
        Assert.Contains("Menu clássico instalado", service.LastInstallMessage);
        Assert.Contains("handler COM atual reinicia o Explorer", service.LastInstallMessage);
    }

    [Fact]
    public void InstallForCurrentUser_WhenModernRegistrationFails_KeepsClassicAndReportsFailure()
    {
        using var directory = new TempDirectory();
        string executablePath = Path.Combine(directory.Path, "UpxSharp.Wpf.exe");
        string shellExtensionPath = Path.Combine(directory.Path, "UpxSharp.Shell.comhost.dll");
        string packagePath = Path.Combine(directory.Path, "UpxSharp.Sparse.msix");
        File.WriteAllText(executablePath, string.Empty);
        File.WriteAllText(shellExtensionPath, string.Empty);
        File.WriteAllText(packagePath, string.Empty);
        var service = CreateService(
            executablePath,
            isSparsePackageInstalled: _ => false,
            registerSparsePackage: _ => throw new InvalidOperationException("certificado inválido"));

        service.InstallForCurrentUser(autoPack: true);

        Assert.Equal(ExplorerIntegrationStatus.ClassicInstalled, service.GetStatus());
        using RegistryKey? commandKey = Registry.CurrentUser.OpenSubKey($@"{_menuKeyPath}\command");
        Assert.NotNull(commandKey);
        Assert.Contains("Menu clássico instalado", service.LastInstallMessage);
        Assert.Contains("handler COM atual reinicia o Explorer", service.LastInstallMessage);
    }

    [Fact]
    public void RemoveForCurrentUser_RemovesNewProblematicLegacyAndShellKeysOnly()
    {
        using RegistryKey sibling = Registry.CurrentUser.CreateSubKey(
            $@"{_baseKeyPath}\ProblematicExefileShell\OtherTool",
            writable: true)!;
        sibling.SetValue(null, "OtherTool");
        using RegistryKey problematicCommand = Registry.CurrentUser.CreateSubKey(
            $@"{_problematicMenuKeyPath}\command",
            writable: true)!;
        problematicCommand.SetValue(null, "problematic");
        using RegistryKey legacyCommand = Registry.CurrentUser.CreateSubKey(
            $@"{_legacyMenuKeyPath}\command",
            writable: true)!;
        legacyCommand.SetValue(null, "legacy");
        using var directory = new TempDirectory();
        string executablePath = Path.Combine(directory.Path, "UpxSharp.Wpf.exe");
        string shellExtensionPath = Path.Combine(directory.Path, "UpxSharp.Shell.comhost.dll");
        File.WriteAllText(executablePath, string.Empty);
        File.WriteAllText(shellExtensionPath, string.Empty);
        bool uninstallSparsePackageCalled = false;
        var service = CreateService(
            executablePath,
            isSparsePackageInstalled: _ => true,
            uninstallSparsePackage: () => uninstallSparsePackageCalled = true);
        service.InstallForCurrentUser(autoPack: true);

        service.RemoveForCurrentUser();

        Assert.Equal(ExplorerIntegrationStatus.NotInstalled, service.GetStatus());
        Assert.Null(Registry.CurrentUser.OpenSubKey(_menuKeyPath));
        Assert.Null(Registry.CurrentUser.OpenSubKey(_wildcardMenuKeyPath));
        Assert.Null(Registry.CurrentUser.OpenSubKey(_problematicMenuKeyPath));
        Assert.Null(Registry.CurrentUser.OpenSubKey(_legacyMenuKeyPath));
        Assert.Null(Registry.CurrentUser.OpenSubKey(_shellExtensionClsidKeyPath));
        Assert.True(uninstallSparsePackageCalled);
        using RegistryKey? other = Registry.CurrentUser.OpenSubKey($@"{_baseKeyPath}\ProblematicExefileShell\OtherTool");
        Assert.NotNull(other);
    }

    [Fact]
    public void GetStatus_WhenShellExtensionRegisteredButSparsePackageMissing_ReturnsClassicInstalled()
    {
        using var directory = new TempDirectory();
        string executablePath = Path.Combine(directory.Path, "UpxSharp.Wpf.exe");
        string shellExtensionPath = Path.Combine(directory.Path, "UpxSharp.Shell.comhost.dll");
        File.WriteAllText(executablePath, string.Empty);
        File.WriteAllText(shellExtensionPath, string.Empty);
        var service = CreateService(
            executablePath,
            isSparsePackageInstalled: _ => false);

        service.InstallForCurrentUser(autoPack: true);

        Assert.Equal(ExplorerIntegrationStatus.ClassicInstalled, service.GetStatus());
    }

    [Fact]
    public void InstallForCurrentUser_WithShellExtensionButMissingSignedMsix_DoesNotRegisterSparsePackage()
    {
        using var directory = new TempDirectory();
        string executablePath = Path.Combine(directory.Path, "UpxSharp.Wpf.exe");
        string shellExtensionPath = Path.Combine(directory.Path, "UpxSharp.Shell.comhost.dll");
        File.WriteAllText(executablePath, string.Empty);
        File.WriteAllText(shellExtensionPath, string.Empty);
        bool registerSparsePackageCalled = false;
        var service = CreateService(
            executablePath,
            registerSparsePackage: _ => registerSparsePackageCalled = true);

        service.InstallForCurrentUser(autoPack: true);

        Assert.False(registerSparsePackageCalled);
        Assert.Equal(ExplorerIntegrationStatus.ClassicInstalled, service.GetStatus());
        Assert.Contains("handler COM atual reinicia o Explorer", service.LastInstallMessage);
    }

    [Fact]
    public void SparsePackageRegistration_HasRequiredFiles_RequiresAppComHostAndSignedMsix()
    {
        using var directory = new TempDirectory();

        Assert.False(SparsePackageRegistration.HasRequiredFiles(directory.Path));

        File.WriteAllText(Path.Combine(directory.Path, "UpxSharp.Wpf.exe"), string.Empty);
        File.WriteAllText(Path.Combine(directory.Path, "UpxSharp.Shell.comhost.dll"), string.Empty);
        File.WriteAllText(Path.Combine(directory.Path, "UpxSharp.Sparse.msix"), string.Empty);

        Assert.True(SparsePackageRegistration.HasRequiredFiles(directory.Path));
    }

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_baseKeyPath, throwOnMissingSubKey: false);
    }

    private ExplorerContextMenuService CreateService(
        string executablePath,
        Func<string, bool>? isSparsePackageInstalled = null,
        Func<string, bool>? hasSparsePackageFiles = null,
        Action<string>? registerSparsePackage = null,
        Action? uninstallSparsePackage = null)
        => new(
            Registry.CurrentUser,
            _menuKeyPath,
            _wildcardMenuKeyPath,
            _problematicMenuKeyPath,
            _legacyMenuKeyPath,
            _shellExtensionClsidKeyPath,
            () => executablePath,
            fileExists: null,
            isSparsePackageInstalled: isSparsePackageInstalled,
            hasSparsePackageFiles: hasSparsePackageFiles ?? SparsePackageRegistration.HasRequiredFiles,
            registerSparsePackage: registerSparsePackage,
            uninstallSparsePackage: uninstallSparsePackage);

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"upxsharp-shell-tests-{Guid.NewGuid():N}");

        public TempDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
