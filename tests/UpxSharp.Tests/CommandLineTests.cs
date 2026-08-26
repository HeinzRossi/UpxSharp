using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using UpxSharp.Core;
using Xunit;

namespace UpxSharp.Tests;

public class CommandLineTests
{
    [Fact]
    public void Parse_WhenArgsEmpty_ThrowsArgumentException()
        => Assert.Throws<ArgumentException>(() => CommandLineParser.Parse([]));

    [Fact]
    public void Parse_PackWithOutputMethodAndLevel_FillsOptions()
    {
        var options = CommandLineParser.Parse(["pack", "input.exe", "-o", "out.exe", "--zstd", "-9", "-f", "-v"]);

        Assert.Equal(UpxCommand.Compress, options.Command);
        Assert.Equal("input.exe", options.InputPath);
        Assert.Equal(["input.exe"], options.InputPaths);
        Assert.Equal("out.exe", options.OutputPath);
        Assert.Equal(CompressionMethod.Zstd, options.Method);
        Assert.Equal(9, options.Level);
        Assert.True(options.Force);
        Assert.True(options.Verbose);
    }

    [Theory]
    [InlineData("-d", UpxCommand.Decompress)]
    [InlineData("--decompress", UpxCommand.Decompress)]
    [InlineData("-t", UpxCommand.Test)]
    [InlineData("--test", UpxCommand.Test)]
    [InlineData("-l", UpxCommand.List)]
    [InlineData("--list", UpxCommand.List)]
    public void Parse_Aliases_SetCommand(string alias, UpxCommand command)
    {
        var options = CommandLineParser.Parse([alias, "input.exe"]);

        Assert.Equal(command, options.Command);
    }

    [Theory]
    [InlineData("--nrv2b", CompressionMethod.Nrv2bLE32)]
    [InlineData("--nrv2d", CompressionMethod.Nrv2dLE32)]
    [InlineData("--nrv2e", CompressionMethod.Nrv2eLE32)]
    [InlineData("--lzma", CompressionMethod.Lzma)]
    public void Parse_MethodFlags_SetMethod(string flag, CompressionMethod method)
    {
        var options = CommandLineParser.Parse(["pack", "input.exe", flag]);

        Assert.Equal(method, options.Method);
    }

    [Fact]
    public void Parse_WhenOptionUnknown_ThrowsArgumentException()
        => Assert.Throws<ArgumentException>(() => CommandLineParser.Parse(["pack", "input.exe", "--bad"]));

    [Fact]
    public void Parse_WhenMultipleInputs_FillsInputPaths()
    {
        var options = CommandLineParser.Parse(["pack", "a.exe", "b.exe"]);

        Assert.Equal("a.exe", options.InputPath);
        Assert.Equal(["a.exe", "b.exe"], options.InputPaths);
    }

    [Fact]
    public void Parse_WhenWildcardInput_PreservesPattern()
    {
        var options = CommandLineParser.Parse(["pack", "*.exe"]);

        Assert.Equal("*.exe", options.InputPath);
        Assert.Equal(["*.exe"], options.InputPaths);
    }

    [Fact]
    public void Parse_WhenOutputWithMultipleInputs_ThrowsArgumentException()
        => Assert.Throws<ArgumentException>(() => CommandLineParser.Parse(["pack", "a.exe", "b.exe", "-o", "out.exe"]));

    [Fact]
    public void Run_List_PrintsDetectedFormat()
    {
        using var temp = TempFile.Create(CreateSyntheticPe(0x014C));
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = CommandLineRunner.Run(["list", temp.Path], output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("win32/pe", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void Run_Test_PacksUnpacksAndCompares()
    {
        using var temp = TempFile.Create(CreateSyntheticPe(0x8664));
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = CommandLineRunner.Run(["test", temp.Path, "--lzma", "-9"], output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("OK:", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void Run_PackWildcard_CreatesOutputForEachMatch()
    {
        using var dir = TempDirectory.Create();
        string first = Path.Combine(dir.Path, "a.exe");
        string second = Path.Combine(dir.Path, "b.exe");
        File.WriteAllBytes(first, CreateSyntheticPe(0x014C));
        File.WriteAllBytes(second, CreateSyntheticPe(0x8664));
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = CommandLineRunner.Run(["pack", Path.Combine(dir.Path, "*.exe"), "--lzma", "-9"], output, error);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.ChangeExtension(first, ".upx")));
        Assert.True(File.Exists(Path.ChangeExtension(second, ".upx")));
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void Run_PackWhenOutputWouldGrowEvenWithForce_FailsAndDeletesOutput()
    {
        using var temp = TempFile.Create(CreateSyntheticPe(0x014C, 0x400));
        string outputPath = Path.ChangeExtension(temp.Path, ".upx");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = CommandLineRunner.Run(["pack", temp.Path, "-o", outputPath, "--lzma", "-9", "--force"], output, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("resultado não ficou menor", error.ToString());
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public void Run_WhenWildcardHasNoMatches_ReturnsError()
    {
        using var dir = TempDirectory.Create();
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = CommandLineRunner.Run(["pack", Path.Combine(dir.Path, "*.exe")], output, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("Nenhum arquivo encontrado", error.ToString());
    }

    private static byte[] CreateSyntheticPe(ushort machine, int size = 0x10000)
    {
        bool is64 = machine == 0x8664;
        byte[] data = new byte[size];
        data[0] = (byte)'M';
        data[1] = (byte)'Z';
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(60), 0x80);

        int peOffset = 0x80;
        data[peOffset] = (byte)'P';
        data[peOffset + 1] = (byte)'E';
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(peOffset + 4), machine);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(peOffset + 6), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(peOffset + 20), is64 ? (ushort)0xF0 : (ushort)0xE0);

        int optional = peOffset + 24;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(optional), is64 ? (ushort)0x20B : (ushort)0x10B);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(optional + 16), 0x1000);
        if (is64)
            BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(optional + 24), 0x140000000);
        else
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(optional + 28), 0x400000);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(optional + 60), 0x200);

        int section = optional + (is64 ? 0xF0 : 0xE0);
        Encoding.ASCII.GetBytes(".text\0\0\0").CopyTo(data.AsSpan(section, 8));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(section + 8), 0x40);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(section + 12), 0x1000);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(section + 16), 0x40);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(section + 20), 0x200);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(section + 36), 0x60000020);
        "UPXSharp CLI test"u8.CopyTo(data.AsSpan(0x200));
        return data;
    }

    private sealed class TempFile : IDisposable
    {
        private TempFile(string path) => Path = path;

        public string Path { get; }

        public static TempFile Create(byte[] data)
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"upxsharp-cli-{Guid.NewGuid():N}.bin");
            File.WriteAllBytes(path, data);
            return new TempFile(path);
        }

        public void Dispose()
        {
            if (File.Exists(Path))
                File.Delete(Path);
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public static TempDirectory Create()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"upxsharp-cli-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
