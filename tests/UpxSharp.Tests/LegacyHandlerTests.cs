using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using UpxSharp.Core;
using UpxSharp.IO;
using UpxSharp.Packers;
using UpxSharp.Packers.Legacy;
using Xunit;

namespace UpxSharp.Tests;

public class LegacyHandlerTests
{
    private const int PayloadOffset = 0x80;
    private const int PayloadSize = 0x40;
    private const int Ps1PayloadOffset = 0x800;

    public static TheoryData<UpxFormat, Type> LegacyFormats => new()
    {
        { UpxFormat.DosExe, typeof(PackDosExe) },
        { UpxFormat.DosCom, typeof(PackDosCom) },
        { UpxFormat.AtariTos, typeof(PackAtari) },
        { UpxFormat.Ps1Exe, typeof(PackPs1) },
    };

    [Theory]
    [MemberData(nameof(LegacyFormats))]
    public void PackMaster_FindPacker_DetectsSyntheticLegacyFormat(UpxFormat format, Type expectedType)
    {
        using var input = TempInput.Create(CreateSyntheticLegacy(format));

        var packer = PackMaster.FindPacker(input.File, new Options { Method = CompressionMethod.Nrv2bLE32, Level = 1 });

        Assert.IsType(expectedType, packer);
    }

    [Theory]
    [MemberData(nameof(LegacyFormats))]
    public void LegacyPacker_PackAndUnpack_RestoresOriginalBytes(UpxFormat format, Type expectedType)
    {
        _ = expectedType;
        byte[] original = CreateSyntheticLegacy(format);
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
    [MemberData(nameof(LegacyFormats))]
    public void PackMaster_FindUnpacker_WhenLegacyPacked_ReturnsExpectedPacker(UpxFormat format, Type expectedType)
    {
        byte[] packedBytes = Pack(CreateSyntheticLegacy(format));
        using var input = TempInput.Create(packedBytes);

        var unpacker = PackMaster.FindUnpacker(input.File, new Options { Method = CompressionMethod.Nrv2bLE32, Level = 1 });

        Assert.IsType(expectedType, unpacker);
    }

    [Theory]
    [MemberData(nameof(LegacyFormats))]
    public void PackMaster_FindUnpacker_WhenLegacyNotPacked_ThrowsNotPackedException(UpxFormat format, Type expectedType)
    {
        _ = expectedType;
        using var input = TempInput.Create(CreateSyntheticLegacy(format));

        Assert.Throws<NotPackedException>(() => PackMaster.FindUnpacker(input.File, new Options()));
    }

    [Theory]
    [MemberData(nameof(LegacyFormats))]
    public void LegacyPacker_Unpack_WhenPayloadTruncated_ThrowsCorruptedPackException(UpxFormat format, Type expectedType)
    {
        _ = expectedType;
        byte[] packedBytes = Pack(CreateSyntheticLegacy(format));
        byte[] truncated = packedBytes[..^1];
        using var input = TempInput.Create(truncated);
        using var output = TempOutput.Create();
        var options = new Options { Method = CompressionMethod.Nrv2bLE32, Level = 1 };
        var unpacker = PackMaster.FindUnpacker(input.File, options);

        Assert.Throws<CorruptedPackException>(() => unpacker.DoUnpack(output.File, options));
    }

    [Theory]
    [MemberData(nameof(LegacyFormats))]
    public void LegacyPacker_Unpack_WhenCompressedChecksumInvalid_ThrowsCorruptedPackException(UpxFormat format, Type expectedType)
    {
        _ = expectedType;
        byte[] packedBytes = Pack(CreateSyntheticLegacy(format));
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

    [Theory]
    [MemberData(nameof(LegacyFormats))]
    public void LegacyPacker_Unpack_WhenUncompressedLengthExceedsPayload_ThrowsCorruptedPackException(UpxFormat format, Type expectedType)
    {
        _ = expectedType;
        byte[] packedBytes = Pack(CreateSyntheticLegacy(format));
        PackHeader.TryFind(packedBytes, out int headerOffset, out var header);
        Assert.NotNull(header);
        header!.ULen = 0x2000;
        Span<byte> headerBytes = stackalloc byte[PackHeader.SerializedSize];
        header.Write(headerBytes);
        headerBytes.CopyTo(packedBytes.AsSpan(headerOffset));
        using var input = TempInput.Create(packedBytes);
        using var output = TempOutput.Create();
        var options = new Options { Method = CompressionMethod.Nrv2bLE32, Level = 1 };
        var unpacker = PackMaster.FindUnpacker(input.File, options);

        Assert.Throws<CorruptedPackException>(() => unpacker.DoUnpack(output.File, options));
    }

    [Fact]
    public void PackMaster_FindPacker_WhenDosExeHasNoScaffoldPayload_ThrowsCantPackException()
    {
        byte[] data = new byte[0x400];
        data[0] = (byte)'M';
        data[1] = (byte)'Z';
        using var input = TempInput.Create(data);

        var exception = Assert.Throws<CantPackException>(
            () => PackMaster.FindPacker(input.File, new Options { Method = CompressionMethod.Nrv2bLE32, Level = 1 }));

        Assert.Contains("Formato", exception.Message);
    }

    [Fact]
    public void DosExePacker_WithMzHeaderPayload_PackAndUnpack_RestoresOriginalBytes()
    {
        byte[] original = CreateRealLikeDosExe();
        using var input = TempInput.Create(original);
        using var packed = TempOutput.Create();
        using var unpacked = TempOutput.Create();
        var options = new Options { Method = CompressionMethod.Nrv2bLE32, Level = 1 };

        var packer = PackMaster.FindPacker(input.File, options);
        Assert.IsType<PackDosExe>(packer);
        packer.DoPack(packed.File, options);
        packed.DisposeFile();

        using var packedInput = TempInput.OpenExisting(packed.Path);
        var unpacker = PackMaster.FindUnpacker(packedInput.File, options);
        unpacker.DoUnpack(unpacked.File, options);
        unpacked.DisposeFile();

        Assert.Equal(original, File.ReadAllBytes(unpacked.Path));
    }

    [Fact]
    public void PackMaster_FindPacker_WhenDosExeContainsInternalUpxMagic_DoesNotTreatAsPacked()
    {
        byte[] original = CreateRealLikeDosExe();
        "UPX!"u8.CopyTo(original.AsSpan(PayloadOffset + 8));
        using var input = TempInput.Create(original);

        var packer = PackMaster.FindPacker(input.File, new Options { Method = CompressionMethod.Nrv2bLE32, Level = 1 });

        Assert.IsType<PackDosExe>(packer);
    }

    [Fact]
    public void DosComPacker_WithRealComFile_PackAndUnpack_RestoresOriginalBytes()
    {
        byte[] original = CreateRealDosCom();
        using var input = TempInput.Create(original, ".com");
        using var packed = TempOutput.Create(".upx");
        using var unpacked = TempOutput.Create(".com");
        var options = new Options { Method = CompressionMethod.Nrv2bLE32, Level = 1 };

        var packer = PackMaster.FindPacker(input.File, options);
        Assert.IsType<PackDosCom>(packer);
        packer.DoPack(packed.File, options);
        packed.DisposeFile();

        using var packedInput = TempInput.OpenExisting(packed.Path);
        var unpacker = PackMaster.FindUnpacker(packedInput.File, options);
        Assert.IsType<PackDosCom>(unpacker);
        unpacker.DoUnpack(unpacked.File, options);
        unpacked.DisposeFile();

        Assert.Equal(original, File.ReadAllBytes(unpacked.Path));
    }

    [Fact]
    public void PackMaster_FindPacker_WhenSmallFileIsNotComExtension_DoesNotDetectDosCom()
    {
        using var input = TempInput.Create(CreateRealDosCom(), ".bin");

        Assert.Throws<CantPackException>(() => PackMaster.FindPacker(input.File, new Options()));
    }

    [Fact]
    public void PackMaster_FindPacker_WhenComStartsWithMz_DoesNotDetectDosCom()
    {
        byte[] data = CreateRealDosCom();
        data[0] = (byte)'M';
        data[1] = (byte)'Z';
        using var input = TempInput.Create(data, ".com");

        Assert.Throws<CantPackException>(() => PackMaster.FindPacker(input.File, new Options()));
    }

    [Fact]
    public void PackMaster_FindPacker_WhenComExceedsMaximumSize_DoesNotDetectDosCom()
    {
        using var input = TempInput.Create(new byte[65536], ".com");

        Assert.Throws<CantPackException>(() => PackMaster.FindPacker(input.File, new Options()));
    }

    [Fact]
    public void AtariPacker_WithRealTosHeader_PackAndUnpack_RestoresOriginalBytes()
    {
        byte[] original = CreateRealAtari();
        using var input = TempInput.Create(original);
        using var packed = TempOutput.Create();
        using var unpacked = TempOutput.Create();
        var options = new Options { Method = CompressionMethod.Nrv2bLE32, Level = 1 };

        var packer = PackMaster.FindPacker(input.File, options);
        Assert.IsType<PackAtari>(packer);
        packer.DoPack(packed.File, options);
        packed.DisposeFile();

        using var packedInput = TempInput.OpenExisting(packed.Path);
        var unpacker = PackMaster.FindUnpacker(packedInput.File, options);
        unpacker.DoUnpack(unpacked.File, options);
        unpacked.DisposeFile();

        Assert.Equal(original, File.ReadAllBytes(unpacked.Path));
    }

    [Fact]
    public void PackMaster_FindPacker_WhenAtariTextAndDataExceedFile_RejectsFile()
    {
        byte[] data = CreateRealAtari();
        WriteUInt32BigEndian(data.AsSpan(2), 0x1000);
        using var input = TempInput.Create(data);

        Assert.Throws<CantPackException>(() => PackMaster.FindPacker(input.File, new Options()));
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

    private static byte[] CreateSyntheticLegacy(UpxFormat format)
    {
        return format switch
        {
            UpxFormat.DosExe => CreateDosExe(),
            UpxFormat.DosCom => CreateDosCom(),
            UpxFormat.AtariTos => CreateAtari(),
            UpxFormat.Ps1Exe => CreatePs1(),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
    }

    private static byte[] CreateDosExe()
    {
        byte[] data = CreateScaffoldWithPayload(0x400, PayloadOffset);
        data[0] = (byte)'M';
        data[1] = (byte)'Z';
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x20), PayloadOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x24), PayloadSize);
        return data;
    }

    private static byte[] CreateRealLikeDosExe()
    {
        byte[] data = CreateScaffoldWithPayload(0x400, PayloadOffset);
        data[0] = (byte)'M';
        data[1] = (byte)'Z';
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x02), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x04), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x08), PayloadOffset / 16);
        return data;
    }

    private static byte[] CreateDosCom()
    {
        byte[] data = CreateScaffoldWithPayload(0x400, PayloadOffset);
        "COM!"u8.CopyTo(data.AsSpan(0));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x10), PayloadOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x14), PayloadSize);
        return data;
    }

    private static byte[] CreateRealDosCom()
    {
        byte[] data = new byte[128];
        byte[] program =
        [
            0xB4, 0x09,       // mov ah, 09h
            0xBA, 0x0C, 0x01, // mov dx, 010Ch
            0xCD, 0x21,       // int 21h
            0xB8, 0x00, 0x4C, // mov ax, 4c00h
            0xCD, 0x21,       // int 21h
        ];
        program.CopyTo(data, 0);
        "UPXSharp DOS COM$"u8.CopyTo(data.AsSpan(program.Length));
        return data;
    }

    private static byte[] CreateAtari()
    {
        byte[] data = CreateScaffoldWithPayload(0x400, PayloadOffset);
        data[0] = 0x60;
        data[1] = 0x1A;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x20), PayloadOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x24), PayloadSize);
        return data;
    }

    private static byte[] CreateRealAtari()
    {
        const int textSize = 48;
        const int dataSize = 16;
        byte[] data = new byte[28 + textSize + dataSize + 8];
        data[0] = 0x60;
        data[1] = 0x1A;
        WriteUInt32BigEndian(data.AsSpan(2), textSize);
        WriteUInt32BigEndian(data.AsSpan(6), dataSize);
        WriteUInt32BigEndian(data.AsSpan(10), 32);
        WriteUInt32BigEndian(data.AsSpan(14), 8);
        for (int i = 28; i < 28 + textSize + dataSize; i++)
            data[i] = (byte)(i * 13);
        return data;
    }

    private static void WriteUInt32BigEndian(Span<byte> destination, uint value)
        => BinaryPrimitives.WriteUInt32BigEndian(destination, value);

    private static byte[] CreatePs1()
    {
        byte[] data = CreateScaffoldWithPayload(Ps1PayloadOffset + PayloadSize, Ps1PayloadOffset);
        "PS-X EXE"u8.CopyTo(data.AsSpan(0));
        return data;
    }

    private static byte[] CreateScaffoldWithPayload(int fileSize, int payloadOffset)
    {
        byte[] data = new byte[fileSize];
        byte[] payload =
        [
            0x90, 0x90, 0xE8, 0x04, 0x00, 0x00, 0x00, 0x90,
            0x90, 0xE9, 0x02, 0x00, 0x00, 0x00, 0x90, 0xC3
        ];
        payload.CopyTo(data.AsSpan(payloadOffset));
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

        public static TempInput Create(byte[] data, string extension = ".bin")
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"upxsharp-legacy-{Guid.NewGuid():N}{extension}");
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

        public static TempOutput Create(string extension = ".bin")
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"upxsharp-legacy-{Guid.NewGuid():N}{extension}");
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
