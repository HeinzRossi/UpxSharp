using Microsoft.Win32;
using System.Diagnostics;
using System.IO;

namespace UpxSharp.Wpf.Services;

public sealed class ExplorerContextMenuService : IExplorerContextMenuService
{
    private const string ContextMenuKeyPath = @"Software\Classes\SystemFileAssociations\.exe\shell\UpxSharp";
    private const string WildcardContextMenuKeyPath = @"Software\Classes\*\shell\UpxSharp";
    private const string ProblematicContextMenuKeyPath = @"Software\Classes\exefile\shell\UpxSharp";
    private const string LegacyContextMenuKeyPath = @"Software\Classes\exefile\shell\CompressWithUpxSharp";
    private const string MenuText = "Comprimir com UpxSharp";
    private const string AppliesToExecutable = @"System.FileExtension:="".exe""";
    private const string ShellExtensionClsid = "{7F5D0D52-7266-4B01-96D0-744153A0BB02}";
    private const string ShellExtensionClsidWithoutBraces = "7F5D0D52-7266-4B01-96D0-744153A0BB02";
    private const string ShellExtensionFileName = "UpxSharp.Shell.comhost.dll";
    private const string ShellExtensionDescription = "UpxSharp Shell Extension";
    private const string SparsePackageName = "UpxSharp.ContextMenu";
    private static readonly bool ModernIntegrationEnabled = false;

    private readonly RegistryKey _rootKey;
    private readonly string _contextMenuKeyPath;
    private readonly string _wildcardContextMenuKeyPath;
    private readonly string _problematicContextMenuKeyPath;
    private readonly string _legacyContextMenuKeyPath;
    private readonly string _shellExtensionClsidKeyPath;
    private readonly Func<string> _appPathProvider;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, bool> _isSparsePackageInstalled;
    private readonly Func<string, bool> _hasSparsePackageFiles;
    private readonly Action<string> _registerSparsePackage;
    private readonly Action _uninstallSparsePackage;

    public string LastInstallMessage { get; private set; } = string.Empty;

    public ExplorerContextMenuService()
        : this(
            Registry.CurrentUser,
            ContextMenuKeyPath,
            WildcardContextMenuKeyPath,
            ProblematicContextMenuKeyPath,
            LegacyContextMenuKeyPath,
            $@"Software\Classes\CLSID\{ShellExtensionClsid}",
            static () => Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? throw new InvalidOperationException("Não foi possível localizar o executável do UpxSharp."),
            File.Exists,
            IsModernPackageRegistered,
            SparsePackageRegistration.HasRequiredFiles,
            SparsePackageRegistration.EnsureRegistered,
            UninstallSparsePackage)
    {
    }

    public ExplorerContextMenuService(
        RegistryKey rootKey,
        string contextMenuKeyPath,
        string? wildcardContextMenuKeyPath = null,
        string? problematicContextMenuKeyPath = null,
        string? legacyContextMenuKeyPath = null,
        string? shellExtensionClsidKeyPath = null,
        Func<string>? appPathProvider = null,
        Func<string, bool>? fileExists = null,
        Func<string, bool>? isSparsePackageInstalled = null,
        Func<string, bool>? hasSparsePackageFiles = null,
        Action<string>? registerSparsePackage = null,
        Action? uninstallSparsePackage = null)
    {
        _rootKey = rootKey;
        _contextMenuKeyPath = contextMenuKeyPath;
        _wildcardContextMenuKeyPath = wildcardContextMenuKeyPath
            ?? @"Software\Classes\*\shell\UpxSharp";
        _problematicContextMenuKeyPath = problematicContextMenuKeyPath
            ?? @"Software\Classes\exefile\shell\UpxSharp";
        _legacyContextMenuKeyPath = legacyContextMenuKeyPath
            ?? $@"{contextMenuKeyPath}_Legacy";
        _shellExtensionClsidKeyPath = shellExtensionClsidKeyPath
            ?? $@"Software\Classes\CLSID\{ShellExtensionClsid}";
        _appPathProvider = appPathProvider
            ?? throw new ArgumentNullException(nameof(appPathProvider));
        _fileExists = fileExists ?? File.Exists;
        _isSparsePackageInstalled = isSparsePackageInstalled ?? (_ => false);
        _hasSparsePackageFiles = hasSparsePackageFiles ?? (_ => false);
        _registerSparsePackage = registerSparsePackage ?? (_ => { });
        _uninstallSparsePackage = uninstallSparsePackage ?? (() => { });
    }

    public void InstallForCurrentUser(bool autoPack)
    {
        string appPath = Path.GetFullPath(_appPathProvider());
        if (!_fileExists(appPath))
            throw new FileNotFoundException("Executável do UpxSharp não encontrado.", appPath);

        string iconPath = $"{appPath},0";
        string appDirectory = Path.GetDirectoryName(appPath)
            ?? throw new InvalidOperationException("Não foi possível localizar o diretório do UpxSharp.");
        string shellExtensionPath = GetShellExtensionPath(appPath);
        bool hasShellExtension = _fileExists(shellExtensionPath);

        RegisterClassicMenu(_contextMenuKeyPath, appPath, iconPath, autoPack, appliesToExecutable: false);
        RegisterClassicMenu(_wildcardContextMenuKeyPath, appPath, iconPath, autoPack, appliesToExecutable: true);

        if (!hasShellExtension)
        {
            LastInstallMessage = "Menu clássico instalado. Integração moderna não instalada: UpxSharp.Shell.comhost.dll não encontrado.";
            return;
        }

        RegisterShellExtension(appPath, iconPath, shellExtensionPath);
        if (TryRegisterSparsePackage(appDirectory, out string modernMessage))
        {
            LastInstallMessage = "Menu clássico e menu moderno instalados.";
        }
        else
        {
            LastInstallMessage = $"Menu clássico instalado. Integração moderna não instalada: {modernMessage}";
        }
    }

    public void RemoveForCurrentUser()
    {
        _rootKey.DeleteSubKeyTree($@"{_contextMenuKeyPath}\command", throwOnMissingSubKey: false);
        _rootKey.DeleteSubKeyTree(_contextMenuKeyPath, throwOnMissingSubKey: false);
        _rootKey.DeleteSubKeyTree($@"{_wildcardContextMenuKeyPath}\command", throwOnMissingSubKey: false);
        _rootKey.DeleteSubKeyTree(_wildcardContextMenuKeyPath, throwOnMissingSubKey: false);
        _rootKey.DeleteSubKeyTree($@"{_problematicContextMenuKeyPath}\command", throwOnMissingSubKey: false);
        _rootKey.DeleteSubKeyTree(_problematicContextMenuKeyPath, throwOnMissingSubKey: false);
        _rootKey.DeleteSubKeyTree($@"{_legacyContextMenuKeyPath}\command", throwOnMissingSubKey: false);
        _rootKey.DeleteSubKeyTree(_legacyContextMenuKeyPath, throwOnMissingSubKey: false);
        _rootKey.DeleteSubKeyTree($@"{_shellExtensionClsidKeyPath}\InprocServer32", throwOnMissingSubKey: false);
        _rootKey.DeleteSubKeyTree(_shellExtensionClsidKeyPath, throwOnMissingSubKey: false);
        TryUninstallSparsePackage();
        LastInstallMessage = "Integração removida.";
    }

    public bool IsInstalledForCurrentUser()
        => GetStatus() != ExplorerIntegrationStatus.NotInstalled;

    public ExplorerIntegrationStatus GetStatus()
    {
        using RegistryKey? menuKey = _rootKey.OpenSubKey(_contextMenuKeyPath);
        using RegistryKey? commandKey = _rootKey.OpenSubKey($@"{_contextMenuKeyPath}\command");
        using RegistryKey? wildcardCommandKey = _rootKey.OpenSubKey($@"{_wildcardContextMenuKeyPath}\command");
        using RegistryKey? shellExtensionKey = _rootKey.OpenSubKey($@"{_shellExtensionClsidKeyPath}\InprocServer32");

        if ((menuKey is not null || wildcardCommandKey is not null)
            && shellExtensionKey is not null
            && _isSparsePackageInstalled(SparsePackageName))
        {
            return ExplorerIntegrationStatus.ShellExtensionInstalled;
        }

        return commandKey is not null || wildcardCommandKey is not null
            ? ExplorerIntegrationStatus.ClassicInstalled
            : ExplorerIntegrationStatus.NotInstalled;
    }

    private static string GetShellExtensionPath(string appPath)
    {
        string directory = Path.GetDirectoryName(appPath)
            ?? throw new InvalidOperationException("Não foi possível localizar o diretório do UpxSharp.");
        return Path.Combine(directory, ShellExtensionFileName);
    }

    private static string BuildExplorerCommand(string appPath, bool autoPack)
        => $"\"{appPath}\" --context-menu \"%1\"";

    private void RegisterShellExtension(string appPath, string iconPath, string shellExtensionPath)
    {
        using RegistryKey clsidKey = _rootKey.CreateSubKey(_shellExtensionClsidKeyPath, writable: true)
            ?? throw new InvalidOperationException("Não foi possível registrar a Shell Extension.");
        clsidKey.SetValue(null, ShellExtensionDescription, RegistryValueKind.String);
        clsidKey.SetValue("AppPath", appPath, RegistryValueKind.String);
        clsidKey.SetValue("IconPath", iconPath, RegistryValueKind.String);

        using RegistryKey inprocKey = clsidKey.CreateSubKey("InprocServer32", writable: true)
            ?? throw new InvalidOperationException("Não foi possível registrar o servidor COM.");
        inprocKey.SetValue(null, shellExtensionPath, RegistryValueKind.String);
        inprocKey.SetValue("ThreadingModel", "Apartment", RegistryValueKind.String);
    }

    private void RegisterClassicMenu(
        string keyPath,
        string appPath,
        string iconPath,
        bool autoPack,
        bool appliesToExecutable)
    {
        using RegistryKey menuKey = _rootKey.CreateSubKey(keyPath, writable: true)
            ?? throw new InvalidOperationException("Não foi possível criar a chave do menu de contexto.");
        menuKey.SetValue(null, string.Empty, RegistryValueKind.String);
        menuKey.SetValue("MUIVerb", MenuText, RegistryValueKind.String);
        menuKey.SetValue("Icon", iconPath, RegistryValueKind.String);
        menuKey.SetValue("MultiSelectModel", "Single", RegistryValueKind.String);
        menuKey.SetValue("Position", "Top", RegistryValueKind.String);

        if (appliesToExecutable)
            menuKey.SetValue("AppliesTo", AppliesToExecutable, RegistryValueKind.String);

        if (menuKey.GetValue("ExplorerCommandHandler") is not null)
            menuKey.DeleteValue("ExplorerCommandHandler", throwOnMissingValue: false);

        using RegistryKey commandKey = menuKey.CreateSubKey("command", writable: true)
            ?? throw new InvalidOperationException("Não foi possível criar o comando do menu de contexto.");
        commandKey.SetValue(null, BuildExplorerCommand(appPath, autoPack), RegistryValueKind.String);
    }

    private void TryUninstallSparsePackage()
    {
        try
        {
            _uninstallSparsePackage();
        }
        catch (Exception)
        {
            // Remoção do registro clássico não deve falhar por problema no pacote moderno.
        }
    }

    private bool TryRegisterSparsePackage(string appDirectory, out string message)
    {
        try
        {
            if (!ModernIntegrationEnabled)
            {
                message = "integração moderna pausada porque o handler COM atual reinicia o Explorer.";
                return false;
            }

            if (!_hasSparsePackageFiles(appDirectory))
            {
                message = "UpxSharp.Sparse.msix assinado não encontrado.";
                return false;
            }

            _registerSparsePackage(appDirectory);
            if (!_isSparsePackageInstalled(SparsePackageName))
            {
                message = "registro executado, mas PackagedCom/ClassIndex não foi detectado.";
                return false;
            }

            message = "ok";
            return true;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
    }

    private static bool IsModernPackageRegistered(string packageName)
    {
        try
        {
            using var process = StartPowerShell(
                "-Command \""
                + $"$package = Get-AppxPackage -Name '{packageName}' -ErrorAction SilentlyContinue; "
                + "if (-not $package) { exit 1 }; "
                + "$packageCom = 'HKLM:\\Software\\Classes\\PackagedCom\\Package\\' + $package.PackageFullName; "
                + $"$classIndex = 'HKLM:\\Software\\Classes\\PackagedCom\\ClassIndex\\{{{ShellExtensionClsidWithoutBraces}}}'; "
                + "if ((Test-Path -LiteralPath $packageCom) -and (Test-Path -LiteralPath $classIndex)) { exit 0 } else { exit 1 }"
                + "\"");
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void UninstallSparsePackage()
    {
        string script = GetToolScriptPath("uninstall-modern-context-menu.ps1");
        RunPowerShellFile(script, string.Empty);
    }

    private static string GetToolScriptPath(string scriptName)
    {
        string? directory = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !string.IsNullOrWhiteSpace(directory); i++)
        {
            string candidate = Path.GetFullPath(Path.Combine(
                directory,
                "..",
                "..",
                "..",
                "..",
                "tools",
                "windows-shell",
                scriptName));
            if (File.Exists(candidate))
                return candidate;

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new FileNotFoundException($"Script '{scriptName}' não encontrado.");
    }

    private static void RunPowerShellFile(string scriptPath, string arguments)
    {
        using var process = StartPowerShell(
            $"-ExecutionPolicy Bypass -File \"{scriptPath}\" {arguments}");
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Falha ao executar '{scriptPath}'.");
    }

    private static Process StartPowerShell(string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
        };

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Não foi possível iniciar powershell.exe.");
    }
}
