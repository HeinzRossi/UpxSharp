using System.Diagnostics;
using System.IO;

namespace UpxSharp.Wpf.Services;

public static class SparsePackageRegistration
{
    public const string PackageName = "UpxSharp.ContextMenu";
    public const string ProviderClsid = "7F5D0D52-7266-4B01-96D0-744153A0BB02";
    public const string PackageFileName = "UpxSharp.Sparse.msix";
    public const string ShellComHostFileName = "UpxSharp.Shell.comhost.dll";
    public const string AppFileName = "UpxSharp.Wpf.exe";

    public static bool HasRequiredFiles(string appDirectory)
    {
        string fullDirectory = Path.GetFullPath(appDirectory);
        return File.Exists(Path.Combine(fullDirectory, AppFileName))
            && File.Exists(Path.Combine(fullDirectory, ShellComHostFileName))
            && File.Exists(Path.Combine(fullDirectory, PackageFileName));
    }

    public static void EnsureRegistered(string appDirectory)
    {
        string fullDirectory = Path.GetFullPath(appDirectory);
        ValidateRequiredFiles(fullDirectory);

        if (IsPackagePresent())
            Remove();

        string packagePath = Path.Combine(fullDirectory, PackageFileName);
        RunPowerShellCommand(
            "$ErrorActionPreference = 'Stop'; "
            + $"Add-AppxPackage -Path {QuotePowerShell(packagePath)} -ExternalLocation {QuotePowerShell(fullDirectory)}");
    }

    public static Task EnsureRegisteredAsync(string appDirectory)
        => Task.Run(() => EnsureRegistered(appDirectory));

    public static bool IsRegistered()
    {
        try
        {
            using Process process = StartPowerShellCommand(
                "$package = Get-AppxPackage -Name '" + PackageName + "' -ErrorAction SilentlyContinue; "
                + "if (-not $package) { exit 1 }; "
                + "$packageCom = 'HKLM:\\Software\\Classes\\PackagedCom\\Package\\' + $package.PackageFullName; "
                + "$classIndex = 'HKLM:\\Software\\Classes\\PackagedCom\\ClassIndex\\{" + ProviderClsid + "}'; "
                + "if ((Test-Path -LiteralPath $packageCom) -and (Test-Path -LiteralPath $classIndex)) { exit 0 } else { exit 1 }");
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsPackagePresent()
    {
        try
        {
            using Process process = StartPowerShellCommand(
                $"if (Get-AppxPackage -Name '{PackageName}' -ErrorAction SilentlyContinue) {{ exit 0 }} else {{ exit 1 }}");
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void Remove()
    {
        using Process process = StartPowerShellCommand(
            "$ErrorActionPreference = 'Stop'; "
            + $"Get-AppxPackage -Name '{PackageName}' -ErrorAction SilentlyContinue | Remove-AppxPackage");
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(BuildPowerShellError(process, $"Falha ao remover o sparse package '{PackageName}'."));
    }

    private static void ValidateRequiredFiles(string appDirectory)
    {
        string appPath = Path.Combine(appDirectory, AppFileName);
        string shellComHostPath = Path.Combine(appDirectory, ShellComHostFileName);
        string packagePath = Path.Combine(appDirectory, PackageFileName);

        if (!File.Exists(appPath))
            throw new FileNotFoundException("UpxSharp.Wpf.exe não encontrado.", appPath);
        if (!File.Exists(shellComHostPath))
            throw new FileNotFoundException("UpxSharp.Shell.comhost.dll não encontrado.", shellComHostPath);
        if (!File.Exists(packagePath))
            throw new FileNotFoundException("UpxSharp.Sparse.msix assinado não encontrado.", packagePath);
    }

    private static void RunPowerShellCommand(string command)
    {
        using Process process = StartPowerShellCommand(command);
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(BuildPowerShellError(process, $"PowerShell falhou com código {process.ExitCode}."));
    }

    private static Process StartPowerShellCommand(string command)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(command);

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Não foi possível iniciar powershell.exe.");
    }

    private static string QuotePowerShell(string value)
        => $"'{value.Replace("'", "''")}'";

    private static string BuildPowerShellError(Process process, string fallback)
    {
        string output = process.StandardOutput.ReadToEnd().Trim();
        string error = process.StandardError.ReadToEnd().Trim();
        string detail = string.Join(
            Environment.NewLine,
            new[] { error, output }.Where(static text => !string.IsNullOrWhiteSpace(text)));

        if (string.IsNullOrWhiteSpace(detail))
            return fallback;

        if (detail.Contains("0x80073CFF", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("sideload", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("licença de desenvolvedor", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("licenca de desenvolvedor", StringComparison.OrdinalIgnoreCase))
        {
            return $"{fallback} O Windows recusou o pacote moderno porque o MSIX não está assinado/confiável ou o sistema bloqueia sideload.";
        }

        if (detail.Contains("0x80080204", StringComparison.OrdinalIgnoreCase))
            return $"{fallback} O manifesto do sparse package é inválido: {detail}";

        return $"{fallback} {detail}";
    }
}
