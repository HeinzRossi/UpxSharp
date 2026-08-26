using UpxSharp.Shell.ExplorerCommand;
using Xunit;

namespace UpxSharp.Shell.Tests;

public sealed class ShellSelectionHelperTests
{
    [Fact]
    public void HasSupportedPaths_WhenNotSlow_AcceptsExeAndRejectsOtherExtensions()
    {
        Assert.True(ShellSelectionHelper.HasSupportedPaths([@"C:\Temp\tool.exe"], okToBeSlow: false));
        Assert.False(ShellSelectionHelper.HasSupportedPaths([@"C:\Temp\library.dll"], okToBeSlow: false));
        Assert.False(ShellSelectionHelper.HasSupportedPaths([@"C:\Temp\readme.txt"], okToBeSlow: false));
        Assert.False(ShellSelectionHelper.HasSupportedPaths([@"C:\Temp\image.png"], okToBeSlow: false));
    }

    [Fact]
    public void HasSupportedPaths_WhenNotSlow_RequiresAllPathsToBeExe()
    {
        Assert.True(ShellSelectionHelper.HasSupportedPaths([@"C:\Temp\a.exe", @"C:\Temp\b.exe"], okToBeSlow: false));
        Assert.False(ShellSelectionHelper.HasSupportedPaths([@"C:\Temp\a.exe", @"C:\Temp\b.txt"], okToBeSlow: false));
    }

    [Fact]
    public void HasSupportedPaths_WhenSlow_RequiresMzHeader()
    {
        string validExe = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.exe");
        string invalidExe = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.exe");

        try
        {
            File.WriteAllBytes(validExe, [0x4D, 0x5A, 0x00, 0x00]);
            File.WriteAllBytes(invalidExe, [0x00, 0x00, 0x00, 0x00]);

            Assert.True(ShellSelectionHelper.HasSupportedPaths([validExe], okToBeSlow: true));
            Assert.False(ShellSelectionHelper.HasSupportedPaths([invalidExe], okToBeSlow: true));
        }
        finally
        {
            File.Delete(validExe);
            File.Delete(invalidExe);
        }
    }

    [Fact]
    public void HasSupportedPaths_ReturnsFalseForEmptySelection()
    {
        Assert.False(ShellSelectionHelper.HasSupportedPaths([], okToBeSlow: false));
    }
}
