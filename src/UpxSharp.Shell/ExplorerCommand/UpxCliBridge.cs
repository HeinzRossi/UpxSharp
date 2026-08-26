using System.Diagnostics;
using System.IO;

namespace UpxSharp.Shell.ExplorerCommand;

internal static class UpxCliBridge
{
    public static async Task<int> RunAsync(string command, IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
            return 0;

        using Process? process = Process.Start(CreateStartInfo(command, paths));
        if (process is null)
            return -1;

        await process.WaitForExitAsync().ConfigureAwait(false);
        return process.ExitCode;
    }

    internal static ProcessStartInfo CreateStartInfo(string command, IReadOnlyList<string> paths)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(AppContext.BaseDirectory, "UpxSharp.Cli.exe"),
            UseShellExecute = false,
            CreateNoWindow = false,
        };

        startInfo.ArgumentList.Add(command);
        foreach (string path in paths)
            startInfo.ArgumentList.Add(path);

        return startInfo;
    }
}
