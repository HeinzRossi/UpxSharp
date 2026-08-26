using System;
using UpxSharp.Core;
using Xunit;

namespace UpxSharp.Tests;

public class OptionsTests
{
    [Fact]
    public void Default_ReturnsExpectedDefaults()
    {
        var options = Options.Default();

        Assert.Equal(UpxCommand.Compress, options.Command);
        Assert.Equal(CompressionMethod.Lzma, options.Method);
        Assert.Equal(7, options.Level);
        Assert.True(options.UseFilters);
        Assert.Equal(0xFF, options.MaxFilter);
        Assert.True(options.Threads >= 1);
        Assert.False(options.Verbose);
        Assert.False(options.Force);
        Assert.False(options.KeepBackup);
        Assert.False(options.TestAfterPack);
        Assert.False(options.ListOnly);
    }

    [Fact]
    public void Validate_WhenValid_DoesNotThrow()
    {
        var options = new Options
        {
            Command = UpxCommand.Compress,
            InputPath = "input.exe",
            OutputPath = "output.exe",
            Method = CompressionMethod.Lzma,
            Level = 9,
            Threads = 1,
        };

        options.Validate();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10)]
    public void Level_WhenOutOfRange_ThrowsArgumentOutOfRangeException(int level)
    {
        var options = new Options();

        Assert.Throws<ArgumentOutOfRangeException>(() => options.Level = level);
    }

    [Fact]
    public void Threads_WhenLessThanOne_ThrowsArgumentOutOfRangeException()
    {
        var options = new Options();

        Assert.Throws<ArgumentOutOfRangeException>(() => options.Threads = 0);
    }

    [Theory]
    [InlineData(UpxCommand.Compress)]
    [InlineData(UpxCommand.Decompress)]
    [InlineData(UpxCommand.Test)]
    [InlineData(UpxCommand.List)]
    [InlineData(UpxCommand.Info)]
    public void Validate_WhenInputPathMissingForFileCommand_ThrowsArgumentException(UpxCommand command)
    {
        var options = new Options { Command = command };

        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Fact]
    public void Validate_WhenMethodInvalid_ThrowsArgumentOutOfRangeException()
    {
        var options = new Options
        {
            Command = UpxCommand.Compress,
            InputPath = "input.exe",
            Method = (CompressionMethod)255,
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }
}
