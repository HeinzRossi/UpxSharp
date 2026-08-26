using System;
using System.IO;
using UpxSharp.IO;
using Xunit;

namespace UpxSharp.Tests;

public class IOTests
{
    [Fact]
    public void InputFile_ReadExact_ReadsExpectedBytesAtOffset()
    {
        using var temp = TempFile.Create([0, 1, 2, 3, 4, 5]);
        using var input = new InputFile(temp.Path);
        byte[] buffer = new byte[3];

        input.ReadExact(buffer, 2);

        Assert.Equal([2, 3, 4], buffer);
    }

    [Fact]
    public void InputFile_ReadExact_WhenBeyondEnd_ThrowsEndOfStreamException()
    {
        using var temp = TempFile.Create([1, 2, 3]);
        using var input = new InputFile(temp.Path);
        byte[] buffer = new byte[4];

        Assert.Throws<EndOfStreamException>(() => input.ReadExact(buffer, 1));
    }

    [Fact]
    public void InputFile_ReadExact_WhenOffsetNegative_ThrowsArgumentOutOfRangeException()
    {
        using var temp = TempFile.Create([1, 2, 3]);
        using var input = new InputFile(temp.Path);

        Assert.Throws<ArgumentOutOfRangeException>(() => input.ReadExact(new byte[1], -1));
    }

    [Fact]
    public void InputFile_Read_WhenOffsetNegative_ThrowsArgumentOutOfRangeException()
    {
        using var temp = TempFile.Create([1, 2, 3]);
        using var input = new InputFile(temp.Path);

        Assert.Throws<ArgumentOutOfRangeException>(() => input.Read(new byte[1], -1));
    }

    [Fact]
    public void OutputFile_Write_WritesBytesAndTracksPosition()
    {
        using var temp = TempFile.ReservePath();
        using (var output = new OutputFile(temp.Path, overwrite: true))
        {
            output.Write([1, 2, 3]);

            Assert.Equal(3, output.Position);
        }

        Assert.Equal([1, 2, 3], File.ReadAllBytes(temp.Path));
    }

    [Fact]
    public void OutputFile_Rewrite_ChangesBytesAndPreservesPosition()
    {
        using var temp = TempFile.ReservePath();
        using (var output = new OutputFile(temp.Path, overwrite: true))
        {
            output.Write([1, 2, 3, 4]);
            output.Rewrite([9, 8], 1);

            Assert.Equal(4, output.Position);
        }

        Assert.Equal([1, 9, 8, 4], File.ReadAllBytes(temp.Path));
    }

    [Fact]
    public void OutputFile_Rewrite_WhenOffsetNegative_ThrowsArgumentOutOfRangeException()
    {
        using var temp = TempFile.ReservePath();
        using var output = new OutputFile(temp.Path, overwrite: true);

        Assert.Throws<ArgumentOutOfRangeException>(() => output.Rewrite([1], -1));
    }

    [Fact]
    public void OutputFile_WhenOverwriteFalseAndFileExists_ThrowsIOException()
    {
        using var temp = TempFile.Create([1]);

        Assert.Throws<IOException>(() => new OutputFile(temp.Path, overwrite: false));
    }

    [Fact]
    public void OutputFile_WhenOverwriteTrue_ReplacesFile()
    {
        using var temp = TempFile.Create([1, 2, 3]);
        using (var output = new OutputFile(temp.Path, overwrite: true))
        {
            output.Write([4]);
        }

        Assert.Equal([4], File.ReadAllBytes(temp.Path));
    }

    private sealed class TempFile : IDisposable
    {
        private TempFile(string path) => Path = path;

        public string Path { get; }

        public static TempFile Create(byte[] data)
        {
            var temp = ReservePath();
            File.WriteAllBytes(temp.Path, data);
            return temp;
        }

        public static TempFile ReservePath()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"upxsharp-io-{Guid.NewGuid():N}.bin");
            return new TempFile(path);
        }

        public void Dispose()
        {
            if (File.Exists(Path))
                File.Delete(Path);
        }
    }
}
