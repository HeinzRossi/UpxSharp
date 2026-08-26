using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using UpxSharp.Core;
using UpxSharp.IO;
using UpxSharp.Packers;
using UpxSharp.Packers.MacOs;
using Xunit;

namespace UpxSharp.Tests;

public class MachHandlerTests
{
    [Theory]
    [InlineData(false, typeof(PackMachI386))]
    [InlineData(true, typeof(PackMachAMD64))]
    public void PackMaster_FindPacker_DetectsSyntheticMach(bool is64, Type expectedType)
    {
        using var input = TempInput.Create(CreateSyntheticMach(is64));

        var packer = PackMaster.FindPacker(input.File, new Options { Method = CompressionMethod.Nrv2bLE32, Level = 1 });

        Assert.IsType(expectedType, packer);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MachPacker_PackAndUnpack_RestoresOriginalBytes(bool is64)
    {
        byte[] original = CreateSyntheticMach(is64);
        using var input = TempInput.Create(original);
        using var packed = TempOutput.Create();
        using var unpacked = TempOutput.Create();
        var options = new Options { Method = CompressionMethod.Nrv2bLE32, Level = 1 };

        var packer = PackMaster.FindPacker(input.File, options);
        packer.DoPack(packed.File, options);
        packed.DisposeFile();

        byte[] packedBytes = File.ReadAllBytes(packed.Path);
        Assert.True(packedBytes.Length > original.Length);
        Assert.Contains("UPX!", Encoding.ASCII.GetString(packedBytes));

        using var packedInput = TempInput.OpenExisting(packed.Path);
        var unpacker = PackMaster.FindUnpacker(packedInput.File, options);
        unpacker.DoUnpack(unpacked.File, options);
        unpacked.DisposeFile();

        Assert.Equal(original, File.ReadAllBytes(unpacked.Path));
    }

    [Theory]
    [InlineData(false, typeof(PackMachI386))]
    [InlineData(true, typeof(PackMachAMD64))]
    public void PackMaster_FindUnpacker_WhenMachPacked_ReturnsExpectedPacker(bool is64, Type expectedType)
    {
        byte[] packedBytes = Pack(CreateSyntheticMach(is64));
        using var input = TempInput.Create(packedBytes);

        var unpacker = PackMaster.FindUnpacker(input.File, new Options { Method = CompressionMethod.Nrv2bLE32, Level = 1 });

        Assert.IsType(expectedType, unpacker);
    }

    [Fact]
    public void PackMaster_FindUnpacker_WhenMachNotPacked_ThrowsNotPackedException()
    {
        using var input = TempInput.Create(CreateSyntheticMach(is64: false));

        Assert.Throws<NotPackedException>(() => PackMaster.FindUnpacker(input.File, new Options()));
    }

    [Fact]
    public void MachPacker_Unpack_WhenPayloadTruncated_ThrowsCorruptedPackException()
    {
        byte[] packedBytes = Pack(CreateSyntheticMach(is64: false));
        byte[] truncated = packedBytes[..^1];
        using var input = TempInput.Create(truncated);
        using var output = TempOutput.Create();
        var options = new Options { Method = CompressionMethod.Nrv2bLE32, Level = 1 };
        var unpacker = PackMaster.FindUnpacker(input.File, options);

        Assert.Throws<CorruptedPackException>(() => unpacker.DoUnpack(output.File, options));
    }

    [Fact]
    public void MachPacker_Unpack_WhenCompressedChecksumInvalid_ThrowsCorruptedPackException()
    {
        byte[] packedBytes = Pack(CreateSyntheticMach(is64: false));
        PackHeader.TryFind(packedBytes, out int headerOffset, out var header);
        Assert.NotNull(header);
        int compressedOffset = headerOffset + PackHeader.SerializedSize;
        packedBytes[compressedOffset] ^= 0x7F;
        using var input = TempInput.Create(packedBytes);
        using var output = TempOutput.Create();
        var options = new Options { Method = CompressionMethod.Nrv2bLE32, Level = 1 };
        var unpacker = PackMaster.FindUnpacker(input.File, options);

        Assert.Throws<CorruptedPackException>(() => unpacker.DoUnpack(output.File, options));
    }

    [Fact]
    public void MachPacker_Unpack_WhenUncompressedLengthExceedsCodeSection_ThrowsCorruptedPackException()
    {
        byte[] packedBytes = Pack(CreateSyntheticMach(is64: false));
        PackHeader.TryFind(packedBytes, out int headerOffset, out var header);
        Assert.NotNull(header);
        header!.ULen = 0x200;
        Span<byte> headerBytes = stackalloc byte[PackHeader.SerializedSize];
        header.Write(headerBytes);
        headerBytes.CopyTo(packedBytes.AsSpan(headerOffset));
        using var input = TempInput.Create(packedBytes);
        using var output = TempOutput.Create();
        var options = new Options { Method = CompressionMethod.Nrv2bLE32, Level = 1 };
        var unpacker = PackMaster.FindUnpacker(input.File, options);

        Assert.Throws<CorruptedPackException>(() => unpacker.DoUnpack(output.File, options));
    }

    private static byte[] Pack(byte[] original)
    {
        using var input = TempInput.Create(original);
        using var packed = TempOutput.Create();
        var options = new Options { Method = CompressionMethod.Nrv2bLE32, Level = 1 };
        var packer = PackMaster.FindPacker(input.File, options);
        packer.DoPack(packed.File, options);
        packed.DisposeFile();
        return File.ReadAllBytes(packed.Path);
    }

    private static byte[] CreateSyntheticMach(bool is64)
    {
        byte[] data = new byte[0x400];
        if (is64)
            WriteMach64(data);
        else
            WriteMach32(data);

        byte[] text =
        [
            0x90, 0x90, 0xE8, 0x04, 0x00, 0x00, 0x00, 0x90,
            0x90, 0xE9, 0x02, 0x00, 0x00, 0x00, 0x90, 0xC3
        ];
        text.CopyTo(data.AsSpan(0x200));
        return data;
    }

    private static void WriteMach32(byte[] data)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0), 0xFEEDFACE);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 7);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20), 124);

        int segment = 28;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(segment), 0x1);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(segment + 4), 124);
        WriteFixedString(data.AsSpan(segment + 8, 16), "__TEXT");
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(segment + 24), 0x1000);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(segment + 28), 0x1000);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(segment + 32), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(segment + 36), 0x400);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(segment + 48), 1);

        int section = segment + 56;
        WriteFixedString(data.AsSpan(section, 16), "__text");
        WriteFixedString(data.AsSpan(section + 16, 16), "__TEXT");
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(section + 32), 0x1000);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(section + 36), 0x40);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(section + 40), 0x200);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(section + 56), 0x80000400);
    }

    private static void WriteMach64(byte[] data)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0), 0xFEEDFACF);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 0x01000007);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20), 152);

        int segment = 32;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(segment), 0x19);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(segment + 4), 152);
        WriteFixedString(data.AsSpan(segment + 8, 16), "__TEXT");
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(segment + 24), 0x100000000);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(segment + 32), 0x1000);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(segment + 40), 0);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(segment + 48), 0x400);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(segment + 64), 1);

        int section = segment + 72;
        WriteFixedString(data.AsSpan(section, 16), "__text");
        WriteFixedString(data.AsSpan(section + 16, 16), "__TEXT");
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(section + 32), 0x100000200);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(section + 40), 0x40);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(section + 48), 0x200);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(section + 64), 0x80000400);
    }

    private static void WriteFixedString(Span<byte> destination, string value)
    {
        destination.Clear();
        Encoding.ASCII.GetBytes(value).CopyTo(destination);
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
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"upxsharp-mach-{Guid.NewGuid():N}.bin");
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
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"upxsharp-mach-{Guid.NewGuid():N}.bin");
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
