using System.Buffers.Binary;
using UpxSharp.Core;

namespace UpxSharp.Official;

public static class OfficialUpxMetadataParser
{
    private const int LInfoSize = 12;
    private const int PInfoSize = 12;
    private const int BInfoSize = 12;

    public static OfficialUpxMetadata Parse(ReadOnlySpan<byte> data, UpxFormat expectedFormat)
    {
        if (!OfficialUpxDetector.TryDetect(data, expectedFormat, out var detected) || detected is null)
            throw new OfficialUpxMetadataException("Arquivo nao contem estrutura UPX oficial reconhecida");

        return detected.Container switch
        {
            "ELF" => ParseElf(data, detected),
            "PE" => ParsePackHeaderContainer(data, detected, GetOfficialFormat(expectedFormat)),
            "DOS EXE" => ParsePackHeaderContainer(data, detected, expectedFormat == UpxFormat.DosExe ? null : GetOfficialFormat(expectedFormat)),
            _ => throw new OfficialUpxMetadataException($"Container UPX oficial sem parser: {detected.Container}")
        };
    }

    private static OfficialUpxMetadata ParsePackHeaderContainer(
        ReadOnlySpan<byte> data,
        OfficialUpxMetadata detected,
        byte? expectedOfficialFormat)
    {
        int searchStart = detected.Container == "PE"
            ? Math.Max(0, detected.SignatureOffset - 0x2000)
            : 0;
        int searchLength = data.Length - searchStart;
        if (!OfficialPackHeaderDecoder.TryFind(
                data,
                searchStart,
                searchLength,
                expectedOfficialFormat,
                out OfficialPackHeader? header)
            || header is null)
            throw new OfficialUpxMetadataException($"UPX oficial {detected.Container} sem PackHeader válido");

        int payloadOffset = checked(header.Offset + header.Size);
        if (header.CompressedLength > int.MaxValue
            || payloadOffset > data.Length - (int)header.CompressedLength)
            throw new OfficialUpxMetadataException($"Payload UPX oficial {detected.Container} truncado");

        var block = new OfficialUpxBlock(
            header.Offset,
            payloadOffset,
            header.UncompressedLength,
            header.CompressedLength,
            header.Method,
            header.FilterId,
            header.FilterCto,
            0);

        return detected with
        {
            SignatureOffset = header.Offset,
            Reason = $"{detected.Container} official PackHeader parsed; container rebuild pending",
            OriginalFileSize = header.OriginalFileSize,
            BlocksOffset = payloadOffset,
            PackedHeaderVersion = header.Version,
            PackedHeaderFormat = header.Format,
            IsBigEndian = header.Format >= 128,
            IsCompleteBlockMap = true,
            Blocks = [block],
            PackHeader = header
        };
    }

    private static OfficialUpxMetadata ParseElf(ReadOnlySpan<byte> data, OfficialUpxMetadata detected)
    {
        if (data.Length < 64 || !data[..4].SequenceEqual("\u007FELF"u8))
            throw new OfficialUpxMetadataException("ELF oficial truncado ou invalido");

        byte elfClass = data[4];
        byte encoding = data[5];
        bool bigEndian = encoding == 2;
        if (encoding is not (1 or 2))
            throw new OfficialUpxMetadataException("ELF oficial com endianness invalido");

        if (elfClass == 1)
        {
            if (data.Length < 52)
                throw new OfficialUpxMetadataException("ELF32 oficial truncado");
        }
        else if (elfClass == 2)
        {
            if (data.Length < 64)
                throw new OfficialUpxMetadataException("ELF64 oficial truncado");
        }
        else
        {
            throw new OfficialUpxMetadataException("ELF oficial com class invalido");
        }

        int lInfoOffset = detected.SignatureOffset - 4;
        if (lInfoOffset < 0 || lInfoOffset + LInfoSize + PInfoSize + BInfoSize > data.Length)
            throw new OfficialUpxMetadataException("ELF oficial sem l_info/p_info/b_info completo");

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(data[(lInfoOffset + 4)..(lInfoOffset + 8)]);
        if (magic != UpxConstants.MagicLE32)
            throw new OfficialUpxMetadataException($"ELF oficial sem l_info valido no offset 0x{lInfoOffset:X}");

        ushort loaderSize = ReadUInt16(data[(lInfoOffset + 8)..(lInfoOffset + 10)], bigEndian);
        byte version = data[lInfoOffset + 10];
        byte format = data[lInfoOffset + 11];
        if (version == 0 || format == 0 || loaderSize == 0)
            throw new OfficialUpxMetadataException("ELF oficial com l_info inconsistente");

        int pInfoOffset = lInfoOffset + LInfoSize;
        uint originalFileSize = ReadUInt32(data[(pInfoOffset + 4)..(pInfoOffset + 8)], bigEndian);
        uint blockSize = ReadUInt32(data[(pInfoOffset + 8)..(pInfoOffset + 12)], bigEndian);
        if (originalFileSize == 0 || blockSize == 0)
            throw new OfficialUpxMetadataException("ELF oficial com p_info inconsistente");

        IReadOnlyList<OfficialElfProgramHeader> packedProgramHeaders =
            ReadProgramHeaders(data, elfClass, bigEndian);
        var packedLoads = packedProgramHeaders.Where(header => header.Type == 1).ToArray();
        bool isCBaseLayout = packedLoads.Length >= 2
            && packedLoads[0].Offset == 0
            && packedLoads[0].FileSize == 0x1000
            && packedLoads[1].Offset == 0
            && packedLoads[1].FileSize == packedLoads[1].MemorySize;
        int loaderEndOffset = isCBaseLayout
            ? checked((int)Align4(packedLoads[1].MemorySize))
            : -1;

        int firstBlockHeaderOffset = pInfoOffset + PInfoSize;
        var firstSequence = ParseBlockSequence(
            data, firstBlockHeaderOffset, blockSize, bigEndian, requireFirstBlock: true);
        var blocks = new List<OfficialUpxBlock>(firstSequence.Blocks);
        bool isCompleteBlockMap = firstSequence.IsComplete;

        if (!isCompleteBlockMap && isCBaseLayout && loaderEndOffset >= 0 && loaderEndOffset < data.Length)
        {
            var gapSequence = ParseBlockSequence(
                data, loaderEndOffset, blockSize, bigEndian, requireFirstBlock: false);
            blocks.AddRange(gapSequence.Blocks);
            isCompleteBlockMap = gapSequence.IsComplete;
        }

        if (blocks.Count == 0)
            throw new OfficialUpxMetadataException("ELF oficial sem blocos de payload");

        return detected with
        {
            SignatureOffset = lInfoOffset + 4,
            Reason = isCompleteBlockMap
                ? "ELF official l_info/p_info/b_info parsed"
                : "ELF official l_info/p_info/first b_info parsed; container rebuild pending",
            OriginalFileSize = originalFileSize,
            BlockSize = blockSize,
            IsBigEndian = bigEndian,
            LoaderInfoOffset = lInfoOffset,
            ProgramInfoOffset = pInfoOffset,
            BlocksOffset = firstBlockHeaderOffset,
            PackedHeaderVersion = version,
            PackedHeaderFormat = format,
            IsCompleteBlockMap = isCompleteBlockMap,
            PackedProgramHeaders = packedProgramHeaders,
            IsCBaseLayout = isCBaseLayout,
            LoaderEndOffset = loaderEndOffset,
            Blocks = blocks
        };
    }

    private static BlockSequenceResult ParseBlockSequence(
        ReadOnlySpan<byte> data,
        int startOffset,
        uint blockSize,
        bool bigEndian,
        bool requireFirstBlock)
    {
        var blocks = new List<OfficialUpxBlock>();
        int blockHeaderOffset = startOffset;

        while (true)
        {
            if (blockHeaderOffset < 0 || blockHeaderOffset + BInfoSize > data.Length)
            {
                if (requireFirstBlock && blocks.Count == 0)
                    throw new OfficialUpxMetadataException("ELF oficial sem primeiro b_info completo");
                return new BlockSequenceResult(blocks, false);
            }

            uint unc = ReadUInt32(data[blockHeaderOffset..(blockHeaderOffset + 4)], bigEndian);
            uint cpr = ReadUInt32(data[(blockHeaderOffset + 4)..(blockHeaderOffset + 8)], bigEndian);
            uint eofMagic = BinaryPrimitives.ReadUInt32LittleEndian(data[(blockHeaderOffset + 4)..(blockHeaderOffset + 8)]);
            if (unc == 0 && eofMagic == UpxConstants.MagicLE32)
                return new BlockSequenceResult(blocks, true);

            if (unc == 0 || cpr == 0 || cpr > unc || unc > blockSize)
            {
                if (requireFirstBlock && blocks.Count == 0)
                    throw new OfficialUpxMetadataException("ELF oficial com primeiro b_info inconsistente");
                return new BlockSequenceResult(blocks, false);
            }

            int payloadOffset = blockHeaderOffset + BInfoSize;
            if (payloadOffset > data.Length || payloadOffset + cpr > data.Length)
            {
                if (requireFirstBlock && blocks.Count == 0)
                    throw new OfficialUpxMetadataException("ELF oficial com payload comprimido truncado");
                return new BlockSequenceResult(blocks, false);
            }

            byte methodByte = data[blockHeaderOffset + 8];
            if (!Enum.IsDefined(typeof(CompressionMethod), methodByte))
            {
                if (requireFirstBlock && blocks.Count == 0)
                    throw new OfficialUpxMetadataException($"Metodo UPX oficial desconhecido: {methodByte}");
                return new BlockSequenceResult(blocks, false);
            }

            blocks.Add(new OfficialUpxBlock(
                blockHeaderOffset,
                payloadOffset,
                unc,
                cpr,
                (CompressionMethod)methodByte,
                data[blockHeaderOffset + 9],
                data[blockHeaderOffset + 10],
                data[blockHeaderOffset + 11]));

            blockHeaderOffset = payloadOffset + checked((int)cpr);
        }
    }

    private static IReadOnlyList<OfficialElfProgramHeader> ReadProgramHeaders(
        ReadOnlySpan<byte> elf,
        byte elfClass,
        bool bigEndian)
    {
        ulong phoff;
        ushort phentsize;
        ushort phnum;
        int minimumEntrySize;

        if (elfClass == 1)
        {
            phoff = ReadUInt32(elf[28..32], bigEndian);
            phentsize = ReadUInt16(elf[42..44], bigEndian);
            phnum = ReadUInt16(elf[44..46], bigEndian);
            minimumEntrySize = 32;
        }
        else
        {
            phoff = ReadUInt64(elf[32..40], bigEndian);
            phentsize = ReadUInt16(elf[54..56], bigEndian);
            phnum = ReadUInt16(elf[56..58], bigEndian);
            minimumEntrySize = 56;
        }

        if (phnum == 0 || phentsize < minimumEntrySize
            || phoff > (ulong)elf.Length
            || phoff + (ulong)phentsize * phnum > (ulong)elf.Length)
            throw new OfficialUpxMetadataException("ELF oficial com program headers invalidos");

        var headers = new List<OfficialElfProgramHeader>(phnum);
        for (int i = 0; i < phnum; i++)
        {
            ReadOnlySpan<byte> span = elf.Slice((int)(phoff + (ulong)i * phentsize), phentsize);
            headers.Add(elfClass == 1
                ? new OfficialElfProgramHeader(
                    ReadUInt32(span[0..4], bigEndian),
                    ReadUInt32(span[4..8], bigEndian),
                    ReadUInt32(span[8..12], bigEndian),
                    ReadUInt32(span[16..20], bigEndian),
                    ReadUInt32(span[20..24], bigEndian),
                    ReadUInt32(span[24..28], bigEndian),
                    ReadUInt32(span[28..32], bigEndian))
                : new OfficialElfProgramHeader(
                    ReadUInt32(span[0..4], bigEndian),
                    ReadUInt64(span[8..16], bigEndian),
                    ReadUInt64(span[16..24], bigEndian),
                    ReadUInt64(span[32..40], bigEndian),
                    ReadUInt64(span[40..48], bigEndian),
                    ReadUInt32(span[4..8], bigEndian),
                    ReadUInt64(span[48..56], bigEndian)));
        }

        return headers;
    }

    private static ulong Align4(ulong value) => (value + 3) & ~3UL;

    private static byte? GetOfficialFormat(UpxFormat format)
        => format switch
        {
            UpxFormat.DosCom => 1,
            UpxFormat.DosExe => 3,
            UpxFormat.Win32Pe => 9,
            UpxFormat.Vmlinuzi386 => 15,
            UpxFormat.Ps1Exe => 18,
            UpxFormat.MachI386 => 29,
            UpxFormat.MachAMD64 => 34,
            UpxFormat.Win64Pe => 36,
            UpxFormat.AtariTos => 129,
            _ => null
        };

    private sealed record BlockSequenceResult(
        IReadOnlyList<OfficialUpxBlock> Blocks,
        bool IsComplete);

    private static ushort ReadUInt16(ReadOnlySpan<byte> source, bool bigEndian)
        => bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(source) : BinaryPrimitives.ReadUInt16LittleEndian(source);

    private static uint ReadUInt32(ReadOnlySpan<byte> source, bool bigEndian)
        => bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(source) : BinaryPrimitives.ReadUInt32LittleEndian(source);

    private static ulong ReadUInt64(ReadOnlySpan<byte> source, bool bigEndian)
        => bigEndian ? BinaryPrimitives.ReadUInt64BigEndian(source) : BinaryPrimitives.ReadUInt64LittleEndian(source);
}
