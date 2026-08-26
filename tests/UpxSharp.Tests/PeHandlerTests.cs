using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using UpxSharp.Core;
using UpxSharp.IO;
using UpxSharp.Packers;
using UpxSharp.Packers.Windows;
using Xunit;

namespace UpxSharp.Tests;

public class PeHandlerTests
{
    [Theory]
    [InlineData(0x014C, typeof(PeFile32))]
    [InlineData(0x8664, typeof(PeFile64))]
    public void PackMaster_FindPacker_DetectsSyntheticPe(ushort machine, Type expectedType)
    {
        using var input = TempInput.Create(CreateSyntheticPe(machine));

        var packer = PackMaster.FindPacker(input.File, new Options { Method = CompressionMethod.Nrv2bLE32, Level = 1 });

        Assert.IsType(expectedType, packer);
    }

    [Theory]
    [InlineData(0x014C)]
    [InlineData(0x8664)]
    public void PePacker_PackAndUnpack_RestoresOriginalBytes(ushort machine)
    {
        byte[] original = CreateSyntheticPe(machine);
        using var input = TempInput.Create(original);
        using var packed = TempOutput.Create();
        using var unpacked = TempOutput.Create();
        var options = new Options { Method = CompressionMethod.Lzma, Level = 9 };

        var packer = PackMaster.FindPacker(input.File, options);
        packer.DoPack(packed.File, options);
        packed.DisposeFile();

        byte[] packedBytes = File.ReadAllBytes(packed.Path);
        Assert.True(packedBytes.Length < original.Length);
        Assert.Contains("UPX!", Encoding.ASCII.GetString(packedBytes));

        using var packedInput = TempInput.OpenExisting(packed.Path);
        var unpacker = PackMaster.FindUnpacker(packedInput.File, options);
        unpacker.DoUnpack(unpacked.File, options);
        unpacked.DisposeFile();

        Assert.Equal(original, File.ReadAllBytes(unpacked.Path));
    }

    [Fact]
    public void PePacker_Unpack_WhenPayloadTruncated_ThrowsCorruptedPackException()
    {
        byte[] packedBytes = Pack(CreateSyntheticPe(0x014C));
        byte[] truncated = packedBytes[..^1];
        using var input = TempInput.Create(truncated);
        using var output = TempOutput.Create();
        var options = new Options { Method = CompressionMethod.Lzma, Level = 9 };
        var unpacker = PackMaster.FindUnpacker(input.File, options);

        Assert.Throws<CorruptedPackException>(() => unpacker.DoUnpack(output.File, options));
    }

    [Fact]
    public void PePacker_Unpack_WhenCompressedChecksumInvalid_ThrowsCorruptedPackException()
    {
        byte[] packedBytes = Pack(CreateSyntheticPe(0x014C));
        PackHeader.TryFind(packedBytes, out int headerOffset, out var header);
        Assert.NotNull(header);
        int compressedOffset = headerOffset + PackHeader.SerializedSize;
        packedBytes[compressedOffset] ^= 0x7F;
        using var input = TempInput.Create(packedBytes);
        using var output = TempOutput.Create();
        var options = new Options { Method = CompressionMethod.Lzma, Level = 9 };
        var unpacker = PackMaster.FindUnpacker(input.File, options);

        Assert.Throws<CorruptedPackException>(() => unpacker.DoUnpack(output.File, options));
    }

    [Fact]
    public void PePacker_Unpack_WhenUncompressedLengthExceedsCodeSection_ThrowsCorruptedPackException()
    {
        byte[] packedBytes = Pack(CreateSyntheticPe(0x014C));
        PackHeader.TryFind(packedBytes, out int headerOffset, out var header);
        Assert.NotNull(header);
        header!.ULen = 0x200;
        Span<byte> headerBytes = stackalloc byte[PackHeader.SerializedSize];
        header.Write(headerBytes);
        headerBytes.CopyTo(packedBytes.AsSpan(headerOffset));
        using var input = TempInput.Create(packedBytes);
        using var output = TempOutput.Create();
        var options = new Options { Method = CompressionMethod.Lzma, Level = 9 };
        var unpacker = PackMaster.FindUnpacker(input.File, options);

        Assert.Throws<CorruptedPackException>(() => unpacker.DoUnpack(output.File, options));
    }

    private static byte[] Pack(byte[] original)
    {
        using var input = TempInput.Create(original);
        using var packed = TempOutput.Create();
        var options = new Options { Method = CompressionMethod.Lzma, Level = 9 };
        var packer = PackMaster.FindPacker(input.File, options);
        packer.DoPack(packed.File, options);
        packed.DisposeFile();
        return File.ReadAllBytes(packed.Path);
    }

    private static byte[] CreateSyntheticPe(ushort machine)
    {
        bool is64 = machine == 0x8664;
        byte[] data = new byte[0x10000];
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

        byte[] text =
        [
            0x90, 0x90, 0xE8, 0x04, 0x00, 0x00, 0x00, 0x90,
            0x90, 0xE9, 0x02, 0x00, 0x00, 0x00, 0x90, 0xC3
        ];
        text.CopyTo(data.AsSpan(0x200));
        return data;
    }

    private sealed class TempInput : IDisposable
    {
        private readonly bool _delete;

        private TempInput(string path, bool delete)
        {
            Path = path;
            _delete = delete;
            File = new InputFile(path);
        }

        public string Path { get; }
        public InputFile File { get; }

        public static TempInput Create(byte[] data)
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"upxsharp-pe-{Guid.NewGuid():N}.bin");
            System.IO.File.WriteAllBytes(path, data);
            return new TempInput(path, delete: true);
        }

        public static TempInput OpenExisting(string path)
            => new(path, delete: false);

        public void Dispose()
        {
            File.Dispose();
            if (_delete)
                System.IO.File.Delete(Path);
        }
    }

    private sealed class TempOutput : IDisposable
    {
        private TempOutput(string path)
        {
            Path = path;
            _file = new OutputFile(path, overwrite: true);
        }

        public string Path { get; }
        public OutputFile File => _file ?? throw new ObjectDisposedException(nameof(TempOutput));
        private OutputFile? _file;

        public static TempOutput Create()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"upxsharp-pe-{Guid.NewGuid():N}.bin");
            return new TempOutput(path);
        }

        public void Dispose()
        {
            DisposeFile();
            System.IO.File.Delete(Path);
        }

        public void DisposeFile()
        {
            _file?.Dispose();
            _file = null;
        }
    }
}
