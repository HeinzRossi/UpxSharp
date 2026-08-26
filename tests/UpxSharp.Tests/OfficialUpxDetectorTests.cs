using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using UpxSharp.Compression;
using UpxSharp.Core;
using UpxSharp.IO;
using UpxSharp.Official;
using UpxSharp.Packers;
using Xunit;

namespace UpxSharp.Tests;

public class OfficialUpxDetectorTests
{
    [Fact]
    public void TryDetect_WhenPeHasUpxSections_ReturnsMetadata()
    {
        byte[] data = CreateOfficialPeLike(machine: 0x8664);

        bool detected = OfficialUpxDetector.TryDetect(data, UpxFormat.Win64Pe, out var metadata);

        Assert.True(detected);
        Assert.NotNull(metadata);
        Assert.Equal(UpxFormat.Win64Pe, metadata!.Format);
        Assert.Equal("PE", metadata.Container);
    }

    [Fact]
    public void TryDetect_WhenElfHasUpxSignatureAndNoSectionTable_ReturnsMetadata()
    {
        byte[] data = CreateOfficialElfLike();

        bool detected = OfficialUpxDetector.TryDetect(data, UpxFormat.LinuxElfAmd64, out var metadata);

        Assert.True(detected);
        Assert.NotNull(metadata);
        Assert.Equal(UpxFormat.LinuxElfAmd64, metadata!.Format);
        Assert.Equal("ELF", metadata.Container);
    }

    [Fact]
    public void PackMaster_FindUnpacker_WhenOfficialPeLike_ReturnsHandlerAndDoUnpackThrowsMetadataError()
    {
        using var input = TempFile.Create(CreateOfficialPeLike(machine: 0x8664));
        using var output = TempFile.CreateEmpty();
        var options = new Options();

        using var inputFile = new InputFile(input.Path);
        var unpacker = PackMaster.FindUnpacker(inputFile, options);

        Assert.Equal(UpxFormat.Win64Pe, unpacker.GetFormat());
        using var outputFile = new OutputFile(output.Path, overwrite: true);
        Assert.Throws<OfficialUpxMetadataException>(() => unpacker.DoUnpack(outputFile, options));
    }

    [Fact]
    public void PackMaster_FindUnpacker_WhenOfficialElfLikeWithMetadata_UnpacksPayloadBlocks()
    {
        byte[] expected = "hello official upx"u8.ToArray();
        using var input = TempFile.Create(CreateOfficialElfLikeWithMetadata(expected));
        using var output = TempFile.CreateEmpty();
        var options = new Options();

        using var inputFile = new InputFile(input.Path);
        var unpacker = PackMaster.FindUnpacker(inputFile, options);

        Assert.Equal(UpxFormat.LinuxElfAmd64, unpacker.GetFormat());
        using (var outputFile = new OutputFile(output.Path, overwrite: true))
            unpacker.DoUnpack(outputFile, options);

        Assert.Equal(expected, File.ReadAllBytes(output.Path));
    }

    [Fact]
    public void PackMaster_FindUnpacker_WhenOfficialElfLikeHasMultipleBlocks_UnpacksAllBlocks()
    {
        byte[] first = "hello "u8.ToArray();
        byte[] second = "multi block upx"u8.ToArray();
        byte[] expected = first.Concat(second).ToArray();
        using var input = TempFile.Create(CreateOfficialElfLikeWithMetadata(first, second));
        using var output = TempFile.CreateEmpty();
        var options = new Options();

        using var inputFile = new InputFile(input.Path);
        var unpacker = PackMaster.FindUnpacker(inputFile, options);

        Assert.Equal(UpxFormat.LinuxElfAmd64, unpacker.GetFormat());
        using (var outputFile = new OutputFile(output.Path, overwrite: true))
            unpacker.DoUnpack(outputFile, options);

        Assert.Equal(expected, File.ReadAllBytes(output.Path));
    }

    [Fact]
    public void PackMaster_FindUnpacker_WhenOfficialDosExeLike_ReturnsHandlerAndDoUnpackThrowsMetadataError()
    {
        using var input = TempFile.Create(CreateOfficialDosExeLike());
        using var output = TempFile.CreateEmpty();
        var options = new Options();

        using var inputFile = new InputFile(input.Path);
        var unpacker = PackMaster.FindUnpacker(inputFile, options);

        Assert.Equal(UpxFormat.DosExe, unpacker.GetFormat());
        using var outputFile = new OutputFile(output.Path, overwrite: true);
        Assert.Throws<OfficialUpxMetadataException>(() => unpacker.DoUnpack(outputFile, options));
    }

    [Fact]
    public void OfficialUpxMetadataParser_WhenElfLInfoMissing_ThrowsMetadataError()
    {
        byte[] data = CreateOfficialElfLike();

        Assert.Throws<OfficialUpxMetadataException>(() => OfficialUpxMetadataParser.Parse(data, UpxFormat.LinuxElfAmd64));
    }

    [Fact]
    public void OfficialUpxMetadataParser_WhenElfLInfoIsNotAtHeaderEnd_ParsesFromSignature()
    {
        byte[] data = CreateOfficialElfLikeWithMetadata("offset metadata"u8.ToArray(), lInfoOffset: 0xE8);

        var metadata = OfficialUpxMetadataParser.Parse(data, UpxFormat.LinuxElfAmd64);

        Assert.Equal(0xE8, metadata.LoaderInfoOffset);
        Assert.Equal(0xF4, metadata.ProgramInfoOffset);
        Assert.Equal(0x100, metadata.BlocksOffset);
        Assert.True(metadata.IsCompleteBlockMap);
        Assert.Single(metadata.Blocks!);
    }

    [Fact]
    public void OfficialUpxMetadataParser_WhenElfMethodUnknown_ThrowsMetadataError()
    {
        byte[] data = CreateOfficialElfLikeWithMetadata("bad method"u8.ToArray());
        int methodOffset = 64 + 56 + 12 + 12 + 8;
        data[methodOffset] = 0xFE;

        Assert.Throws<OfficialUpxMetadataException>(() => OfficialUpxMetadataParser.Parse(data, UpxFormat.LinuxElfAmd64));
    }

    [Fact]
    public void OfficialUpxMetadataParser_WhenElfPayloadTruncated_ThrowsMetadataError()
    {
        byte[] data = CreateOfficialElfLikeWithMetadata("truncated"u8.ToArray());
        int cprOffset = 64 + 56 + 12 + 12 + 4;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(cprOffset, 4), 100_000);

        Assert.Throws<OfficialUpxMetadataException>(() => OfficialUpxMetadataParser.Parse(data, UpxFormat.LinuxElfAmd64));
    }

    [Fact]
    public void OfficialUpxUnpacker_ReadMetadata_WhenFirstBlockHasOriginalElfHeaders_PopulatesProgramHeaders()
    {
        byte[] originalElf = CreateOriginalElf64(machine: 62, phnum: 1);
        byte[] data = CreateOfficialElfLikeWithMetadata(originalElf);

        var metadata = OfficialUpxUnpacker.ReadMetadata(data, UpxFormat.LinuxElfAmd64);

        Assert.True(metadata.HasOriginalElfHeaders);
        Assert.Equal(2, metadata.OriginalElfClass);
        Assert.Equal(62, metadata.OriginalMachine);
        Assert.Equal(0x401000ul, metadata.OriginalEntry);
        var phdr = Assert.Single(metadata.OriginalProgramHeaders!);
        Assert.Equal(1u, phdr.Type);
        Assert.Equal(0x1000ul, phdr.Offset);
        Assert.Equal(0x400000ul, phdr.VirtualAddress);
        Assert.Equal(0x200ul, phdr.FileSize);
    }

    [Fact]
    public void OfficialUpxUnpacker_ReadMetadata_WhenOriginalElfMachineDiffers_ThrowsMetadataError()
    {
        byte[] originalElf = CreateOriginalElf64(machine: 183, phnum: 1);
        byte[] data = CreateOfficialElfLikeWithMetadata(originalElf);

        Assert.Throws<OfficialUpxMetadataException>(() => OfficialUpxUnpacker.ReadMetadata(data, UpxFormat.LinuxElfAmd64));
    }

    [Fact]
    public void OfficialUpxUnpacker_ReadMetadata_WhenOriginalProgramHeadersTruncated_ThrowsMetadataError()
    {
        byte[] originalElf = CreateOriginalElf64(machine: 62, phnum: 4);
        byte[] data = CreateOfficialElfLikeWithMetadata(originalElf);

        Assert.Throws<OfficialUpxMetadataException>(() => OfficialUpxUnpacker.ReadMetadata(data, UpxFormat.LinuxElfAmd64));
    }

    [Fact]
    public void OfficialUpxUnpacker_ReadMetadata_WhenFirstBlockCorrupted_ThrowsDecompressError()
    {
        byte[] data = CreateOfficialElfLikeWithCorruptedCompressedBlock();

        Assert.Throws<OfficialUpxDecompressException>(() => OfficialUpxUnpacker.ReadMetadata(data, UpxFormat.LinuxElfAmd64));
    }

    [Fact]
    public void OfficialUpxUnpacker_WhenElfUsesCBaseLayout_RebuildsLoadsGapsAndTail()
    {
        (byte[] packed, byte[] original) = CreateOfficialCBaseElf64();

        byte[] unpacked = OfficialUpxUnpacker.Unpack(packed, UpxFormat.LinuxElfAmd64);

        Assert.Equal(original, unpacked);
        var metadata = OfficialUpxUnpacker.ReadMetadata(packed, UpxFormat.LinuxElfAmd64);
        Assert.True(metadata.IsCBaseLayout);
        Assert.True(metadata.IsCompleteBlockMap);
        Assert.Equal(0x400, metadata.LoaderEndOffset);
        Assert.Equal(4, metadata.Blocks!.Count);
    }

    [Fact]
    public void OfficialUpxExtentReader_WhenBlockOvershootsExtent_ThrowsMetadataError()
    {
        byte[] data = "01234567"u8.ToArray();
        var blocks = new[]
        {
            new OfficialUpxBlock(0, 0, 8, 8, CompressionMethod.Nrv2bLE32, 0, 0, 0),
        };
        var reader = new OfficialUpxExtentReader(data, blocks);

        Assert.Throws<OfficialUpxMetadataException>(() => reader.ReadExtent(4));
    }

    private static byte[] CreateOfficialPeLike(ushort machine)
    {
        byte[] data = new byte[1024];
        data[0] = (byte)'M';
        data[1] = (byte)'Z';
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x3C, 4), 0x80);
        "PE\0\0"u8.CopyTo(data.AsSpan(0x80));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x84, 2), machine);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x86, 2), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x94, 2), 0xF0);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x98, 2), machine == 0x8664 ? (ushort)0x20b : (ushort)0x10b);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x98 + 16, 4), 0x1000);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x98 + 60, 4), 0x200);

        int sectionTable = 0x80 + 24 + 0xF0;
        "UPX0"u8.CopyTo(data.AsSpan(sectionTable));
        "UPX1"u8.CopyTo(data.AsSpan(sectionTable + 40));
        "UPX!"u8.CopyTo(data.AsSpan(0x300));
        return data;
    }

    private static byte[] CreateOfficialElfLike()
    {
        byte[] data = new byte[1024];
        data[0] = 0x7F;
        data[1] = (byte)'E';
        data[2] = (byte)'L';
        data[3] = (byte)'F';
        data[4] = 2;
        data[5] = 1;
        data[6] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(18, 2), 62);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(32, 8), 64);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(56, 2), 3);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(40, 8), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(60, 2), 0);
        "UPX!"u8.CopyTo(data.AsSpan(256));
        "$Info:"u8.CopyTo(data.AsSpan(512));
        return data;
    }

    private static byte[] CreateOfficialElfLikeWithMetadata(byte[] payload, int? lInfoOffset = null)
        => CreateOfficialElfLikeWithMetadata(new[] { payload }, lInfoOffset);

    private static byte[] CreateOfficialElfLikeWithMetadata(byte[] firstPayload, byte[] secondPayload, int? lInfoOffset = null)
        => CreateOfficialElfLikeWithMetadata(new[] { firstPayload, secondPayload }, lInfoOffset);

    private static byte[] CreateOfficialElfLikeWithMetadata(byte[][] payloads, int? lInfoOffset = null)
    {
        int actualLInfoOffset = lInfoOffset ?? 64 + 56;
        int pInfoOffset = actualLInfoOffset + 12;
        int bInfoOffset = pInfoOffset + 12;
        int totalPayloadLength = payloads.Sum(payload => payload.Length);
        int blockPayloadBytes = payloads.Sum(payload => 12 + payload.Length);
        int eofOffset = bInfoOffset + blockPayloadBytes;
        byte[] data = new byte[eofOffset + 12 + 64];

        data[0] = 0x7F;
        data[1] = (byte)'E';
        data[2] = (byte)'L';
        data[3] = (byte)'F';
        data[4] = 2;
        data[5] = 1;
        data[6] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(16, 2), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(18, 2), 62);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(32, 8), 64);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(40, 8), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(52, 2), 64);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(54, 2), 56);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(56, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(58, 2), 64);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(60, 2), 0);

        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(actualLInfoOffset, 4), 0);
        "UPX!"u8.CopyTo(data.AsSpan(actualLInfoOffset + 4));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(actualLInfoOffset + 8, 2), 12);
        data[actualLInfoOffset + 10] = 52;
        data[actualLInfoOffset + 11] = (byte)UpxFormat.LinuxElfAmd64;

        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(pInfoOffset, 4), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(pInfoOffset + 4, 4), (uint)totalPayloadLength);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(pInfoOffset + 8, 4), (uint)payloads.Max(payload => payload.Length));

        int cursor = bInfoOffset;
        foreach (byte[] payload in payloads)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(cursor, 4), (uint)payload.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(cursor + 4, 4), (uint)payload.Length);
            data[cursor + 8] = (byte)CompressionMethod.Nrv2bLE32;
            payload.CopyTo(data.AsSpan(cursor + 12));
            cursor += 12 + payload.Length;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(eofOffset, 4), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(eofOffset + 4, 4), UpxConstants.MagicLE32);
        "$Info:"u8.CopyTo(data.AsSpan(eofOffset + 12));

        return data;
    }

    private static byte[] CreateOfficialElfLikeWithCorruptedCompressedBlock()
    {
        byte[] data = CreateOfficialElfLikeWithMetadata(CreateOriginalElf64(machine: 62, phnum: 1));
        int bInfoOffset = 64 + 56 + 12 + 12;
        int payloadOffset = bInfoOffset + 12;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(bInfoOffset, 4), 64);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(bInfoOffset + 4, 4), 1);
        data[bInfoOffset + 8] = (byte)CompressionMethod.Nrv2bLE32;
        data[payloadOffset] = 0;
        return data;
    }

    private static (byte[] Packed, byte[] Original) CreateOfficialCBaseElf64()
    {
        const int originalSize = 0x300;
        const int firstLoadSize = 0x100;
        const int secondLoadOffset = 0x200;
        const int secondLoadSize = 0x80;
        const int loaderInfoOffset = 0x100;
        const int loaderEndOffset = 0x400;

        byte[] original = new byte[originalSize];
        for (int i = 0; i < original.Length; i++)
            original[i] = (byte)(i * 17 + 3);

        WriteElf64Header(original, phnum: 2);
        WriteElf64Load(original, 64, 0, firstLoadSize, flags: 5);
        WriteElf64Load(original, 120, secondLoadOffset, secondLoadSize, flags: 6);

        byte[] packed = new byte[0x600];
        WriteElf64Header(packed, phnum: 3);
        WriteElf64Load(packed, 64, 0, 0x1000, flags: 6, memorySize: 0x3000);
        WriteElf64Load(packed, 120, 0, loaderEndOffset, flags: 5, memorySize: loaderEndOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(packed.AsSpan(176, 4), 0x6474E551);
        "$Info:"u8.CopyTo(packed.AsSpan(0xF0));

        "UPX!"u8.CopyTo(packed.AsSpan(loaderInfoOffset + 4));
        BinaryPrimitives.WriteUInt16LittleEndian(packed.AsSpan(loaderInfoOffset + 8, 2), 12);
        packed[loaderInfoOffset + 10] = 52;
        packed[loaderInfoOffset + 11] = (byte)UpxFormat.LinuxElfAmd64;
        BinaryPrimitives.WriteUInt32LittleEndian(packed.AsSpan(loaderInfoOffset + 16, 4), originalSize);
        BinaryPrimitives.WriteUInt32LittleEndian(packed.AsSpan(loaderInfoOffset + 20, 4), firstLoadSize);

        int cursor = loaderInfoOffset + 24;
        cursor = WriteLiteralBlock(packed, cursor, original.AsSpan(0, firstLoadSize));
        _ = WriteLiteralBlock(packed, cursor, original.AsSpan(secondLoadOffset, secondLoadSize));

        cursor = loaderEndOffset;
        cursor = WriteLiteralBlock(packed, cursor, original.AsSpan(firstLoadSize, secondLoadOffset - firstLoadSize));
        cursor = WriteLiteralBlock(packed, cursor, original.AsSpan(secondLoadOffset + secondLoadSize));
        BinaryPrimitives.WriteUInt32LittleEndian(packed.AsSpan(cursor, 4), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(packed.AsSpan(cursor + 4, 4), UpxConstants.MagicLE32);

        return (packed, original);
    }

    private static int WriteLiteralBlock(byte[] destination, int offset, ReadOnlySpan<byte> payload)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(offset, 4), (uint)payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(offset + 4, 4), (uint)payload.Length);
        destination[offset + 8] = (byte)CompressionMethod.Nrv2bLE32;
        payload.CopyTo(destination.AsSpan(offset + 12));
        return offset + 12 + payload.Length;
    }

    private static void WriteElf64Header(byte[] data, ushort phnum)
    {
        data[0] = 0x7F;
        data[1] = (byte)'E';
        data[2] = (byte)'L';
        data[3] = (byte)'F';
        data[4] = 2;
        data[5] = 1;
        data[6] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(16, 2), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(18, 2), 62);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20, 4), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(24, 8), 0x401000);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(32, 8), 64);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(52, 2), 64);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(54, 2), 56);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(56, 2), phnum);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(58, 2), 64);
    }

    private static void WriteElf64Load(
        byte[] data,
        int offset,
        ulong fileOffset,
        ulong fileSize,
        uint flags,
        ulong? memorySize = null)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset + 4, 4), flags);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset + 8, 8), fileOffset);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset + 16, 8), 0x400000 + fileOffset);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset + 24, 8), 0x400000 + fileOffset);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset + 32, 8), fileSize);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset + 40, 8), memorySize ?? fileSize);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset + 48, 8), 0x1000);
    }

    private static byte[] CreateOriginalElf64(ushort machine, ushort phnum)
    {
        byte[] data = new byte[64 + 56];
        data[0] = 0x7F;
        data[1] = (byte)'E';
        data[2] = (byte)'L';
        data[3] = (byte)'F';
        data[4] = 2;
        data[5] = 1;
        data[6] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(16, 2), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(18, 2), machine);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20, 4), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(24, 8), 0x401000);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(32, 8), 64);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(52, 2), 64);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(54, 2), 56);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(56, 2), phnum);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(58, 2), 64);

        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(64, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(68, 4), 5);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(72, 8), 0x1000);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(80, 8), 0x400000);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(88, 8), 0x400000);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(96, 8), 0x200);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(104, 8), 0x300);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(112, 8), 0x1000);
        return data;
    }

    private static byte[] CreateOfficialDosExeLike()
    {
        byte[] data = new byte[4096];
        data[0] = (byte)'M';
        data[1] = (byte)'Z';
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x02, 2), 512);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x04, 2), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x08, 2), 4);
        "UPX!"u8.CopyTo(data.AsSpan(0x500));
        "$Info:"u8.CopyTo(data.AsSpan(0x600));
        return data;
    }

    private sealed class TempFile : IDisposable
    {
        private TempFile(string path) => Path = path;

        public string Path { get; }

        public static TempFile CreateEmpty()
            => new(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"upxsharp-official-{Guid.NewGuid():N}.bin"));

        public static TempFile Create(byte[] bytes)
        {
            var file = CreateEmpty();
            File.WriteAllBytes(file.Path, bytes);
            return file;
        }

        public void Dispose()
        {
            if (File.Exists(Path))
                File.Delete(Path);
        }
    }
}
