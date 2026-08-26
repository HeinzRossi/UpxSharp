using UpxSharp.Shell.ExplorerCommand;
using Xunit;

namespace UpxSharp.Shell.Tests;

public sealed class UpxCliBridgeTests
{
    [Fact]
    public void CreateStartInfo_UsesCliBesideShellAndArgumentList()
    {
        string[] paths = [@"C:\Temp\a.exe", @"C:\Temp\name with spaces.exe"];

        var startInfo = UpxCliBridge.CreateStartInfo("compress", paths);

        Assert.EndsWith("UpxSharp.Cli.exe", startInfo.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.False(startInfo.UseShellExecute);
        Assert.False(startInfo.CreateNoWindow);
        Assert.Equal(["compress", paths[0], paths[1]], startInfo.ArgumentList);
    }
}
