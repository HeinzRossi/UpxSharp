using UpxSharp.Core;
using System.Buffers.Binary;
using UpxSharp.Filters;

namespace UpxSharp.Official;

public static class OfficialUpxUnpacker
{
    public static OfficialUpxMetadata ReadMetadata(ReadOnlySpan<byte> data, UpxFormat expectedFormat)
    {
        OfficialUpxMetadata metadata = OfficialUpxMetadataParser.Parse(data, expectedFormat);
        if (metadata.Container != "ELF" || metadata.Blocks is null || metadata.Blocks.Count == 0)
            return metadata;

        byte[] firstBlock = OfficialUpxExtentReader.DecompressBlock(data, metadata.Blocks[0]);
        if (!firstBlock.AsSpan().StartsWith("\u007FELF"u8))
            return metadata;

        byte originalClass = firstBlock[4];
        ushort originalMachine = ReadOriginalElfMachine(firstBlock, metadata.IsBigEndian);
        ulong originalEntry = ReadOriginalElfEntry(firstBlock, metadata.IsBigEndian);
        var originalProgramHeaders = ReadOriginalProgramHeaders(firstBlock, metadata.IsBigEndian);
        ValidateExpectedOriginalElf(expectedFormat, originalClass, originalMachine);

        return metadata with
        {
            HasOriginalElfHeaders = true,
            OriginalElfClass = originalClass,
            OriginalMachine = originalMachine,
            OriginalEntry = originalEntry,
            OriginalProgramHeaders = originalProgramHeaders
        };
    }

    public static byte[] Unpack(ReadOnlySpan<byte> data, UpxFormat expectedFormat)
    {
        OfficialUpxMetadata metadata = ReadMetadata(data, expectedFormat);
        if (metadata.Blocks is null || metadata.Blocks.Count == 0)
            throw new OfficialUpxMetadataException("UPX oficial sem blocos de payload");

        if (metadata.Container == "ELF" && metadata.IsCBaseLayout)
            return RebuildCBaseElf(data, metadata);
        if (metadata.PackHeader?.Format == 4)
            return RebuildDjgpp2(data, metadata);
        if (metadata.Container == "PE")
            return RebuildPe(data, metadata);

        using var output = new MemoryStream();
        foreach (var block in metadata.Blocks)
        {
            byte[] decompressed = OfficialUpxExtentReader.DecompressBlock(data, block);
            output.Write(decompressed, 0, decompressed.Length);
        }

        byte[] result = output.ToArray();
        if (!metadata.IsCompleteBlockMap || (metadata.OriginalFileSize != 0 && result.Length != metadata.OriginalFileSize))
        {
            string pending = metadata.HasOriginalElfHeaders ? "elf segment rebuild pending" : "container rebuild pending";
            throw new OfficialUpxMetadataException(
                $"UPX oficial {metadata.Container} parseado; {pending}: payload={result.Length}, original={metadata.OriginalFileSize}");
        }

        return result;
    }

    private static byte[] RebuildPe(
        ReadOnlySpan<byte> data,
        OfficialUpxMetadata metadata)
    {
        OfficialPackHeader header = metadata.PackHeader
            ?? throw new OfficialUpxMetadataException("PE oficial sem PackHeader");
        if (metadata.Blocks is null || metadata.Blocks.Count != 1)
            throw new OfficialUpxMetadataException("PE oficial com mapa de blocos inválido");

        VerifyCompressedPayload(data, header);
        byte[] payload = OfficialUpxExtentReader.DecompressBlock(
            data,
            metadata.Blocks[0] with { FilterId = 0 });
        Adler32.Verify(payload, header.UncompressedAdler, "payload UPX oficial PE");
        if (payload.Length < 4)
            throw new OfficialUpxMetadataException("Payload PE oficial truncado");

        uint extraOffsetValue = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(payload.Length - 4, 4));
        if (extraOffsetValue > int.MaxValue)
            throw new OfficialUpxMetadataException("Offset de metadata PE oficial inválido");
        int extraOffset = (int)extraOffsetValue;
        if (extraOffset < 0 || extraOffset > payload.Length - 24)
            throw new OfficialUpxMetadataException("Metadata PE oficial truncada");
        if (!payload.AsSpan(extraOffset, 4).SequenceEqual("PE\0\0"u8))
            throw new OfficialUpxMetadataException("Header PE original ausente no payload");

        ushort sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(extraOffset + 6, 2));
        ushort optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(extraOffset + 20, 2));
        int peHeaderSize = checked(24 + optionalSize);
        int sectionTableOffset = checked(extraOffset + peHeaderSize);
        int sectionTableSize = checked(sectionCount * 40);
        if (sectionCount == 0 || sectionTableOffset > payload.Length - sectionTableSize)
            throw new OfficialUpxMetadataException("Tabela de seções PE original truncada");

        Span<byte> optional = payload.AsSpan(extraOffset + 24, optionalSize);
        if (optional.Length < 64)
            throw new OfficialUpxMetadataException("Optional header PE original truncado");
        uint sizeOfCode = BinaryPrimitives.ReadUInt32LittleEndian(optional[4..8]);
        uint baseOfCode = BinaryPrimitives.ReadUInt32LittleEndian(optional[20..24]);
        uint fileAlignment = BinaryPrimitives.ReadUInt32LittleEndian(optional[36..40]);
        if (fileAlignment == 0)
            throw new OfficialUpxMetadataException("FileAlignment PE original inválido");

        var sections = new List<PeSection>(sectionCount);
        uint virtualBase = uint.MaxValue;
        int outputLength = 0;
        for (int index = 0; index < sectionCount; index++)
        {
            ReadOnlySpan<byte> section = payload.AsSpan(sectionTableOffset + index * 40, 40);
            uint virtualAddress = BinaryPrimitives.ReadUInt32LittleEndian(section[12..16]);
            uint rawSize = BinaryPrimitives.ReadUInt32LittleEndian(section[16..20]);
            uint rawOffset = BinaryPrimitives.ReadUInt32LittleEndian(section[20..24]);
            sections.Add(new PeSection(virtualAddress, rawSize, rawOffset));
            if (rawOffset != 0)
            {
                virtualBase = Math.Min(virtualBase, virtualAddress);
                uint alignedSize = AlignUp(rawSize, fileAlignment);
                outputLength = Math.Max(outputLength, checked((int)(rawOffset + alignedSize)));
            }
        }

        if (virtualBase == uint.MaxValue)
            throw new OfficialUpxMetadataException("PE original sem seções graváveis");

        ApplyOfficialFilterToSlice(
            payload,
            checked((int)(baseOfCode - virtualBase)),
            checked((int)sizeOfCode),
            header.FilterId,
            header.FilterCto,
            baseOfCode - virtualBase);

        int peOffset = ReadPeOffset(data);
        IReadOnlyList<PeSection> packedSections = ReadPeSections(data, peOffset);
        int extraCursor = checked(sectionTableOffset + sectionTableSize);
        bool is64Bit = BinaryPrimitives.ReadUInt16LittleEndian(optional[..2]) == 0x20B;
        RebuildPeImports(
            payload,
            ref extraCursor,
            optional,
            virtualBase,
            data,
            packedSections,
            is64Bit);
        RebuildPeRelocations(
            payload,
            ref extraCursor,
            optional,
            virtualBase,
            BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(extraOffset + 22, 2)),
            is64Bit);

        // UPX deliberately clears volatile PE fields while rebuilding.
        BinaryPrimitives.WriteUInt32LittleEndian(optional[64..68], 0);
        ClearPeDataDirectory(optional, is64Bit, 11); // bound imports

        int packedOverlayOffset = GetPackedPeOverlayOffset(data, peOffset);
        int overlaySize = Math.Max(0, data.Length - packedOverlayOffset);
        outputLength = checked(outputLength + overlaySize);
        if (header.OriginalFileSize != 0)
            outputLength = checked((int)header.OriginalFileSize);

        byte[] result = new byte[outputLength];
        data[..peOffset].CopyTo(result);
        payload.AsSpan(extraOffset, peHeaderSize + sectionTableSize).CopyTo(result.AsSpan(peOffset));

        foreach (PeSection section in sections)
        {
            if (section.RawOffset == 0)
                continue;

            int sourceOffset = checked((int)(section.VirtualAddress - virtualBase));
            int copyLength = checked((int)AlignUp(section.RawSize, fileAlignment));
            if (sourceOffset < 0 || sourceOffset > payload.Length - copyLength)
                throw new OfficialUpxMetadataException("Seção PE original aponta para fora do payload");
            if (section.RawOffset > result.Length - copyLength)
                throw new OfficialUpxMetadataException("Seção PE original aponta para fora do arquivo");

            payload.AsSpan(sourceOffset, copyLength).CopyTo(result.AsSpan((int)section.RawOffset));
        }

        if (overlaySize > 0)
        {
            if (overlaySize > result.Length)
                throw new OfficialUpxMetadataException("Overlay PE oficial inválido");
            data[packedOverlayOffset..].CopyTo(result.AsSpan(result.Length - overlaySize));
        }

        return result;
    }

    private static void RebuildPeImports(
        byte[] payload,
        ref int extraCursor,
        Span<byte> optional,
        uint virtualBase,
        ReadOnlySpan<byte> packedFile,
        IReadOnlyList<PeSection> packedSections,
        bool is64Bit)
    {
        (uint importRva, uint importSize) = ReadPeDataDirectory(optional, is64Bit, 1);
        if (importRva == 0 || importSize <= 20)
            return;
        if (extraCursor < 0 || extraCursor > payload.Length - 8)
            throw new OfficialUpxMetadataException("Metadata de imports PE truncada");

        int compactOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(extraCursor, 4)));
        uint namesRva = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(extraCursor + 4, 4));
        extraCursor += 8;
        if (compactOffset < 0 || compactOffset > payload.Length - 4)
            throw new OfficialUpxMetadataException("Tabela compacta de imports PE inválida");
        if (packedSections.Count < 3)
            throw new OfficialUpxMetadataException("PE compactado sem seção de imports");

        PeSection importSection = packedSections[2];
        if (importSection.RawOffset > packedFile.Length
            || importSection.RawSize > packedFile.Length - importSection.RawOffset)
            throw new OfficialUpxMetadataException("Seção de imports PE compactada truncada");
        ReadOnlySpan<byte> packedImports = packedFile.Slice(
            (int)importSection.RawOffset,
            (int)importSection.RawSize);
        uint packedImportRva = ReadPackedPeDataDirectory(packedFile, 1).Address;
        if (packedImportRva < importSection.VirtualAddress
            || packedImportRva - importSection.VirtualAddress > int.MaxValue)
            throw new OfficialUpxMetadataException("Diretório de imports PE compactado inválido");
        int packedImportBase = (int)(packedImportRva - importSection.VirtualAddress);

        int pointerSize = is64Bit ? 8 : 4;
        ulong ordinalMask = is64Bit ? 1UL << 63 : 1UL << 31;
        int dllNameBytes = 0;
        int cursor = compactOffset;
        while (ReadUInt32(payload, cursor) != 0)
        {
            uint nameOffset = PackedImportRelativeToOffset(
                ReadUInt32(payload, cursor),
                packedImportBase,
                packedImports.Length);
            dllNameBytes = checked(dllNameBytes + ReadNullTerminatedLength(packedImports, nameOffset));
            cursor += 8;
            SkipCompactImportEntries(payload, ref cursor);
            cursor++;
        }
        dllNameBytes = checked((dllNameBytes + 1) & ~1);

        int descriptorOffset = RvaToPayloadOffset(importRva, virtualBase, payload.Length);
        int dllNameOffset = namesRva == 0
            ? -1
            : RvaToPayloadOffset(namesRva, virtualBase, payload.Length);
        int importedNamesStart = dllNameOffset < 0 ? -1 : checked(dllNameOffset + dllNameBytes);
        int importedNamesOffset = importedNamesStart;

        cursor = compactOffset;
        int descriptorIndex = 0;
        while (ReadUInt32(payload, cursor) != 0)
        {
            uint nameOffset = PackedImportRelativeToOffset(
                ReadUInt32(payload, cursor),
                packedImportBase,
                packedImports.Length);
            uint iatRva = checked(ReadUInt32(payload, cursor + 4) + virtualBase);
            ReadOnlySpan<byte> dllName = ReadNullTerminated(packedImports, nameOffset);
            int currentDescriptor = checked(descriptorOffset + descriptorIndex * 20);
            EnsureRange(payload.Length, currentDescriptor, 20, "Descriptor de import PE truncado");

            if (dllNameOffset >= 0)
            {
                EnsureRange(payload.Length, dllNameOffset, dllName.Length, "Nome de DLL PE fora do payload");
                dllName.CopyTo(payload.AsSpan(dllNameOffset));
                BinaryPrimitives.WriteUInt32LittleEndian(
                    payload.AsSpan(currentDescriptor + 12, 4),
                    checked((uint)dllNameOffset + virtualBase));
                dllNameOffset += dllName.Length;
            }
            else
            {
                uint existingNameRva = ReadUInt32(payload, currentDescriptor + 12);
                int existingNameOffset = RvaToPayloadOffset(existingNameRva, virtualBase, payload.Length);
                EnsureRange(payload.Length, existingNameOffset, dllName.Length, "Nome de DLL PE fora do payload");
                dllName.CopyTo(payload.AsSpan(existingNameOffset));
            }

            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(currentDescriptor + 16, 4), iatRva);
            cursor += 8;
            int iatOffset = RvaToPayloadOffset(iatRva, virtualBase, payload.Length);
            while (payload[cursor] != 0)
            {
                ulong value;
                if (payload[cursor] == 1)
                {
                    cursor++;
                    ReadOnlySpan<byte> importName = ReadNullTerminated(payload, cursor);
                    if (importedNamesOffset >= 0)
                    {
                        if (((importedNamesOffset - importedNamesStart) & 1) != 0)
                            importedNamesOffset--;
                        EnsureRange(payload.Length, importedNamesOffset, checked(2 + importName.Length), "Nome importado PE fora do payload");
                        payload[importedNamesOffset] = 0;
                        payload[importedNamesOffset + 1] = 0;
                        importName.CopyTo(payload.AsSpan(importedNamesOffset + 2));
                        value = checked((ulong)importedNamesOffset + virtualBase);
                        importedNamesOffset += 2 + importName.Length;
                    }
                    else
                    {
                        ulong existing = ReadPointer(payload, iatOffset, is64Bit);
                        int destination = RvaToPayloadOffset(checked((uint)existing + 2), virtualBase, payload.Length);
                        EnsureRange(payload.Length, destination, importName.Length, "Nome importado PE fora do payload");
                        importName.CopyTo(payload.AsSpan(destination));
                        value = existing;
                    }
                    cursor += importName.Length;
                }
                else if (payload[cursor] == 0xFF)
                {
                    value = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(cursor + 1, 2)) | ordinalMask;
                    cursor += 3;
                }
                else
                {
                    uint packedOffset = PackedImportRelativeToOffset(
                        ReadUInt32(payload, cursor + 1),
                        packedImportBase,
                        packedImports.Length);
                    EnsureRange(packedImports.Length, checked((int)packedOffset), pointerSize, "Thunk importado PE truncado");
                    value = is64Bit
                        ? BinaryPrimitives.ReadUInt64LittleEndian(packedImports[(int)packedOffset..])
                        : BinaryPrimitives.ReadUInt32LittleEndian(packedImports[(int)packedOffset..]);
                    cursor += 5;
                }

                WritePointer(payload, iatOffset, value, is64Bit);
                iatOffset += pointerSize;
            }
            WritePointer(payload, iatOffset, 0, is64Bit);
            cursor++;
            descriptorIndex++;
        }
    }

    private static void RebuildPeRelocations(
        byte[] payload,
        ref int extraCursor,
        Span<byte> optional,
        uint virtualBase,
        ushort characteristics,
        bool is64Bit)
    {
        (uint relocRva, uint relocSize) = ReadPeDataDirectory(optional, is64Bit, 5);
        if (relocRva == 0 || relocSize == 0 || (characteristics & 1) != 0)
            return;
        if (relocSize == 8)
        {
            int destination = RvaToPayloadOffset(relocRva, virtualBase, payload.Length);
            new byte[] { 0, 0, 0, 0, 8, 0, 0, 0 }.CopyTo(payload, destination);
            return;
        }
        if (extraCursor < 0 || extraCursor > payload.Length - 5)
            throw new OfficialUpxMetadataException("Metadata de relocations PE truncada");

        uint relocationStreamOffset = ReadUInt32(payload, extraCursor);
        extraCursor += 4;
        byte flags = payload[extraCursor++];
        if (relocationStreamOffset >= payload.Length)
            throw new OfficialUpxMetadataException("Stream de relocations PE inválido");

        var relocations = DecodeOptimizedRelocations(
            payload,
            checked((int)relocationStreamOffset),
            is64Bit);
        ulong imageBase = is64Bit
            ? BinaryPrimitives.ReadUInt64LittleEndian(optional[24..32])
            : BinaryPrimitives.ReadUInt32LittleEndian(optional[28..32]);
        foreach (int relocation in relocations)
        {
            if (is64Bit)
            {
                ulong swapped = BinaryPrimitives.ReadUInt64BigEndian(payload.AsSpan(relocation, 8));
                BinaryPrimitives.WriteUInt64LittleEndian(
                    payload.AsSpan(relocation, 8),
                    unchecked(swapped + imageBase + virtualBase));
            }
            else
            {
                uint swapped = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(relocation, 4));
                BinaryPrimitives.WriteUInt32LittleEndian(
                    payload.AsSpan(relocation, 4),
                    unchecked(swapped + (uint)imageBase + virtualBase));
            }
        }

        var relocationEntries = relocations
            .Select(offset => (Rva: checked((uint)offset + virtualBase), Type: is64Bit ? 10 : 3))
            .ToList();
        if ((flags & 6) != 0)
            throw new OfficialUpxUnsupportedException("Relocations PE de 16 bits ainda não suportadas");

        byte[] directory = BuildBaseRelocationDirectory(relocationEntries);
        int directoryOffset = RvaToPayloadOffset(relocRva, virtualBase, payload.Length);
        EnsureRange(payload.Length, directoryOffset, directory.Length, "Diretório de relocations PE fora do payload");
        directory.CopyTo(payload, directoryOffset);
        WritePeDataDirectory(optional, is64Bit, 5, relocRva, (uint)directory.Length);
    }

    private static byte[] RebuildDjgpp2(
        ReadOnlySpan<byte> data,
        OfficialUpxMetadata metadata)
    {
        OfficialPackHeader header = metadata.PackHeader
            ?? throw new OfficialUpxMetadataException("DJGPP2 oficial sem PackHeader");
        if (metadata.Blocks is null || metadata.Blocks.Count != 1)
            throw new OfficialUpxMetadataException("DJGPP2 oficial com mapa de blocos inválido");
        if (data.Length < 64 || !data[..2].SequenceEqual("MZ"u8))
            throw new OfficialUpxMetadataException("DJGPP2 oficial sem stub MZ");

        int lastPageBytes = BinaryPrimitives.ReadUInt16LittleEndian(data[2..4]);
        int pageCount = BinaryPrimitives.ReadUInt16LittleEndian(data[4..6]);
        int coffOffset = checked(pageCount * 512 + (lastPageBytes == 0 ? 0 : lastPageBytes - 512));
        if (coffOffset < 64 || coffOffset > data.Length)
            throw new OfficialUpxMetadataException("Offset COFF DJGPP2 inválido");

        OfficialUpxBlock rawBlock = metadata.Blocks[0] with { FilterId = 0 };
        VerifyCompressedPayload(data, header);
        byte[] payload = OfficialUpxExtentReader.DecompressBlock(data, rawBlock);
        Adler32.Verify(payload, header.UncompressedAdler, "payload UPX oficial DJGPP2");
        if (payload.Length < 168)
            throw new OfficialUpxMetadataException("Payload COFF DJGPP2 truncado");

        ushort sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(2, 2));
        int headerSize = checked(20 + 28 + sectionCount * 40);
        if (sectionCount < 3 || headerSize > payload.Length)
            throw new OfficialUpxMetadataException("Header COFF DJGPP2 inválido");

        const int sectionTableOffset = 48;
        uint textVirtualAddress = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(sectionTableOffset + 12, 4));
        uint textRawOffset = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(sectionTableOffset + 20, 4));
        uint dataSize = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(sectionTableOffset + 40 + 16, 4));
        if (textRawOffset < headerSize || textRawOffset > int.MaxValue || dataSize > payload.Length)
            throw new OfficialUpxMetadataException("Seções COFF DJGPP2 inválidas");

        int filteredLength = checked(payload.Length - (int)dataSize);
        if (filteredLength < 0)
            throw new OfficialUpxMetadataException("Faixa filtrada DJGPP2 inválida");
        ApplyOfficialFilter(
            payload,
            filteredLength,
            header.FilterId,
            header.FilterCto,
            checked(textVirtualAddress - (uint)headerSize));

        int padding = checked((int)textRawOffset - headerSize);
        int rebuiltLength = checked(coffOffset + payload.Length + padding);
        if (header.OriginalFileSize != 0 && rebuiltLength != header.OriginalFileSize)
            throw new OfficialUpxMetadataException(
                $"Tamanho DJGPP2 reconstruído divergente: obtido={rebuiltLength}, esperado={header.OriginalFileSize}");

        byte[] result = new byte[rebuiltLength];
        data[..coffOffset].CopyTo(result);
        payload.AsSpan(0, headerSize).CopyTo(result.AsSpan(coffOffset));
        payload.AsSpan(headerSize).CopyTo(result.AsSpan(coffOffset + headerSize + padding));
        return result;
    }

    private static void VerifyCompressedPayload(
        ReadOnlySpan<byte> data,
        OfficialPackHeader header)
    {
        int payloadOffset = checked(header.Offset + header.Size);
        if (header.CompressedLength > int.MaxValue
            || payloadOffset > data.Length - (int)header.CompressedLength)
            throw new OfficialUpxMetadataException("Payload UPX oficial truncado");

        Adler32.Verify(
            data.Slice(payloadOffset, (int)header.CompressedLength),
            header.CompressedAdler,
            "payload comprimido UPX oficial");
    }

    private static void ApplyOfficialFilter(
        byte[] payload,
        int length,
        byte filterId,
        byte filterCto,
        uint addValue)
    {
        if (filterId == 0)
            return;

        IFilter filter = FilterFactory.Create(filterId)
            ?? throw new OfficialUpxMetadataException($"Filtro UPX oficial desconhecido: 0x{filterId:X2}");
        try
        {
            filter.Unfilter(new FilterState
            {
                Id = filterId,
                Buffer = payload,
                BufLen = length,
                Cto = filterCto,
                AddValue = addValue,
            });
        }
        catch (Exception ex) when (ex is UpxException or ArgumentException or InvalidOperationException)
        {
            throw new OfficialUpxDecompressException(
                $"Falha ao aplicar filtro UPX oficial 0x{filterId:X2}: {ex.Message}");
        }
    }

    private static void ApplyOfficialFilterToSlice(
        byte[] payload,
        int offset,
        int length,
        byte filterId,
        byte filterCto,
        uint addValue)
    {
        if (filterId == 0)
            return;
        if (offset < 0 || length < 0 || offset > payload.Length - length)
            throw new OfficialUpxMetadataException("Faixa filtrada PE oficial inválida");

        byte[] slice = payload.AsSpan(offset, length).ToArray();
        ApplyOfficialFilter(slice, slice.Length, filterId, filterCto, addValue);
        slice.CopyTo(payload, offset);
    }

    private static int ReadPeOffset(ReadOnlySpan<byte> data)
    {
        if (data.Length < 64 || !data[..2].SequenceEqual("MZ"u8))
            throw new OfficialUpxMetadataException("Stub MZ PE oficial inválido");
        uint offset = BinaryPrimitives.ReadUInt32LittleEndian(data[0x3C..0x40]);
        if (offset > int.MaxValue || offset > data.Length - 24)
            throw new OfficialUpxMetadataException("Offset PE oficial inválido");
        return (int)offset;
    }

    private static int GetPackedPeOverlayOffset(ReadOnlySpan<byte> data, int peOffset)
    {
        ushort sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(data[(peOffset + 6)..(peOffset + 8)]);
        ushort optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(data[(peOffset + 20)..(peOffset + 22)]);
        int optionalOffset = peOffset + 24;
        if (optionalOffset > data.Length - optionalSize || optionalSize < 40)
            throw new OfficialUpxMetadataException("Optional header PE compactado truncado");
        uint fileAlignment = BinaryPrimitives.ReadUInt32LittleEndian(data[(optionalOffset + 36)..(optionalOffset + 40)]);
        if (fileAlignment == 0)
            throw new OfficialUpxMetadataException("FileAlignment PE compactado inválido");

        int tableOffset = checked(optionalOffset + optionalSize);
        if (sectionCount == 0 || tableOffset > data.Length - sectionCount * 40)
            throw new OfficialUpxMetadataException("Tabela de seções PE compactado truncada");

        ReadOnlySpan<byte> last = data.Slice(tableOffset + (sectionCount - 1) * 40, 40);
        uint rawSize = BinaryPrimitives.ReadUInt32LittleEndian(last[16..20]);
        uint rawOffset = BinaryPrimitives.ReadUInt32LittleEndian(last[20..24]);
        uint end = AlignUp(checked(rawOffset + rawSize), fileAlignment);
        if (end > data.Length)
            throw new OfficialUpxMetadataException("Overlay PE compactado inválido");
        return (int)end;
    }

    private static IReadOnlyList<PeSection> ReadPeSections(ReadOnlySpan<byte> data, int peOffset)
    {
        ushort sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(data[(peOffset + 6)..(peOffset + 8)]);
        ushort optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(data[(peOffset + 20)..(peOffset + 22)]);
        int tableOffset = checked(peOffset + 24 + optionalSize);
        if (sectionCount == 0 || tableOffset > data.Length - sectionCount * 40)
            throw new OfficialUpxMetadataException("Tabela de seções PE truncada");

        var sections = new List<PeSection>(sectionCount);
        for (int index = 0; index < sectionCount; index++)
        {
            ReadOnlySpan<byte> section = data.Slice(tableOffset + index * 40, 40);
            sections.Add(new PeSection(
                BinaryPrimitives.ReadUInt32LittleEndian(section[12..16]),
                BinaryPrimitives.ReadUInt32LittleEndian(section[16..20]),
                BinaryPrimitives.ReadUInt32LittleEndian(section[20..24])));
        }
        return sections;
    }

    private static (uint Address, uint Size) ReadPeDataDirectory(
        ReadOnlySpan<byte> optional,
        bool is64Bit,
        int index)
    {
        int offset = checked((is64Bit ? 112 : 96) + index * 8);
        if (offset > optional.Length - 8)
            throw new OfficialUpxMetadataException("Diretório PE truncado");
        return (
            BinaryPrimitives.ReadUInt32LittleEndian(optional[offset..(offset + 4)]),
            BinaryPrimitives.ReadUInt32LittleEndian(optional[(offset + 4)..(offset + 8)]));
    }

    private static void WritePeDataDirectory(
        Span<byte> optional,
        bool is64Bit,
        int index,
        uint address,
        uint size)
    {
        int offset = checked((is64Bit ? 112 : 96) + index * 8);
        if (offset > optional.Length - 8)
            throw new OfficialUpxMetadataException("Diretório PE truncado");
        BinaryPrimitives.WriteUInt32LittleEndian(optional[offset..(offset + 4)], address);
        BinaryPrimitives.WriteUInt32LittleEndian(optional[(offset + 4)..(offset + 8)], size);
    }

    private static void ClearPeDataDirectory(Span<byte> optional, bool is64Bit, int index)
        => WritePeDataDirectory(optional, is64Bit, index, 0, 0);

    private static int RvaToPayloadOffset(uint rva, uint virtualBase, int payloadLength)
    {
        if (rva < virtualBase || rva - virtualBase > int.MaxValue)
            throw new OfficialUpxMetadataException($"RVA PE inválido: 0x{rva:X}");
        int offset = (int)(rva - virtualBase);
        if (offset > payloadLength)
            throw new OfficialUpxMetadataException($"RVA PE fora do payload: 0x{rva:X}");
        return offset;
    }

    private static uint PackedImportRelativeToOffset(
        uint relativeOffset,
        int importBase,
        int sectionLength)
    {
        long offset = (long)importBase + relativeOffset;
        if (offset < 0 || offset >= sectionLength)
            throw new OfficialUpxMetadataException(
                $"Offset de import PE compactado inválido: 0x{relativeOffset:X}");
        return (uint)offset;
    }

    private static (uint Address, uint Size) ReadPackedPeDataDirectory(
        ReadOnlySpan<byte> packedFile,
        int index)
    {
        int peOffset = ReadPeOffset(packedFile);
        int optionalOffset = peOffset + 24;
        ushort optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(
            packedFile[(peOffset + 20)..(peOffset + 22)]);
        if (optionalOffset > packedFile.Length - optionalSize || optionalSize < 2)
            throw new OfficialUpxMetadataException("Optional header PE compactado truncado");
        ReadOnlySpan<byte> optional = packedFile.Slice(optionalOffset, optionalSize);
        bool is64Bit = BinaryPrimitives.ReadUInt16LittleEndian(optional[..2]) == 0x20B;
        return ReadPeDataDirectory(optional, is64Bit, index);
    }

    private static uint ReadUInt32(byte[] data, int offset)
    {
        EnsureRange(data.Length, offset, 4, "Leitura PE fora do payload");
        return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
    }

    private static ReadOnlySpan<byte> ReadNullTerminated(byte[] data, int offset)
    {
        EnsureRange(data.Length, offset, 1, "String PE fora do payload");
        int end = offset;
        while (end < data.Length && data[end] != 0)
            end++;
        if (end == data.Length)
            throw new OfficialUpxMetadataException("String PE sem terminador");
        return data.AsSpan(offset, end - offset + 1);
    }

    private static ReadOnlySpan<byte> ReadNullTerminated(ReadOnlySpan<byte> data, uint offset)
    {
        if (offset >= data.Length)
            throw new OfficialUpxMetadataException("String PE fora da seção");
        int start = (int)offset;
        int end = start;
        while (end < data.Length && data[end] != 0)
            end++;
        if (end == data.Length)
            throw new OfficialUpxMetadataException("String PE sem terminador");
        return data[start..(end + 1)];
    }

    private static int ReadNullTerminatedLength(ReadOnlySpan<byte> data, uint offset)
        => ReadNullTerminated(data, offset).Length;

    private static void SkipCompactImportEntries(byte[] payload, ref int cursor)
    {
        while (true)
        {
            EnsureRange(payload.Length, cursor, 1, "Tabela compacta de imports PE truncada");
            byte marker = payload[cursor];
            if (marker == 0)
                return;
            if (marker == 1)
            {
                cursor++;
                cursor += ReadNullTerminated(payload, cursor).Length;
            }
            else if (marker == 0xFF)
            {
                EnsureRange(payload.Length, cursor, 3, "Ordinal importado PE truncado");
                cursor += 3;
            }
            else
            {
                EnsureRange(payload.Length, cursor, 5, "Thunk importado PE truncado");
                cursor += 5;
            }
        }
    }

    private static ulong ReadPointer(byte[] payload, int offset, bool is64Bit)
    {
        EnsureRange(payload.Length, offset, is64Bit ? 8 : 4, "IAT PE truncada");
        return is64Bit
            ? BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(offset, 8))
            : BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset, 4));
    }

    private static void WritePointer(byte[] payload, int offset, ulong value, bool is64Bit)
    {
        EnsureRange(payload.Length, offset, is64Bit ? 8 : 4, "IAT PE truncada");
        if (is64Bit)
            BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(offset, 8), value);
        else
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset, 4), checked((uint)value));
    }

    private static List<int> DecodeOptimizedRelocations(
        byte[] payload,
        int streamOffset,
        bool is64Bit)
    {
        var relocations = new List<int>();
        int cursor = streamOffset;
        uint position = uint.MaxValue - 3;
        while (true)
        {
            EnsureRange(payload.Length, cursor, 1, "Stream de relocations PE truncado");
            byte marker = payload[cursor];
            if (marker == 0)
                break;

            uint delta;
            if (marker < 0xF0)
            {
                delta = marker;
                cursor++;
            }
            else
            {
                EnsureRange(payload.Length, cursor, 3, "Stream de relocations PE truncado");
                delta = checked((uint)(marker & 0x0F) * 0x10000
                    + BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(cursor + 1, 2)));
                cursor += 3;
                if (delta == 0)
                {
                    EnsureRange(payload.Length, cursor, 4, "Stream de relocations PE truncado");
                    delta = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(cursor, 4));
                    cursor += 4;
                }
            }

            if (delta < 4)
                throw new OfficialUpxMetadataException("Relocations PE sobrepostas");
            position = unchecked(position + delta);
            int width = is64Bit ? 8 : 4;
            if (position > int.MaxValue || position > payload.Length - width)
                throw new OfficialUpxMetadataException("Relocation PE fora do payload");

            relocations.Add((int)position);
        }
        return relocations;
    }

    private static byte[] BuildBaseRelocationDirectory(
        IReadOnlyCollection<(uint Rva, int Type)> relocations)
    {
        using var output = new MemoryStream();
        byte[] blockHeader = new byte[8];
        byte[] entry = new byte[2];
        byte[] size = new byte[4];
        foreach (var page in relocations
                     .OrderBy(item => item.Rva)
                     .GroupBy(item => item.Rva & ~0xFFFu))
        {
            long blockStart = output.Position;
            Array.Clear(blockHeader);
            BinaryPrimitives.WriteUInt32LittleEndian(blockHeader.AsSpan(0, 4), page.Key);
            output.Write(blockHeader);
            foreach ((uint rva, int type) in page)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(
                    entry,
                    checked((ushort)(((uint)type << 12) | (rva & 0xFFFu))));
                output.Write(entry);
            }
            while (((output.Position - blockStart) & 3) != 0)
                output.WriteByte(0);

            long end = output.Position;
            output.Position = blockStart + 4;
            BinaryPrimitives.WriteUInt32LittleEndian(size, checked((uint)(end - blockStart)));
            output.Write(size);
            output.Position = end;
        }
        return output.ToArray();
    }

    private static void EnsureRange(int totalLength, int offset, int length, string message)
    {
        if (offset < 0 || length < 0 || offset > totalLength - length)
            throw new OfficialUpxMetadataException(message);
    }

    private static uint AlignUp(uint value, uint alignment)
        => checked((value + alignment - 1) / alignment * alignment);

    private sealed record PeSection(
        uint VirtualAddress,
        uint RawSize,
        uint RawOffset);

    private static byte[] RebuildCBaseElf(
        ReadOnlySpan<byte> data,
        OfficialUpxMetadata metadata)
    {
        if (!metadata.IsCompleteBlockMap)
            throw new OfficialUpxMetadataException("Layout ELF C_BASE sem mapa completo de extents");
        if (!metadata.HasOriginalElfHeaders || metadata.OriginalProgramHeaders is null)
            throw new OfficialUpxMetadataException("Layout ELF C_BASE sem headers originais");
        if (metadata.OriginalFileSize == 0 || metadata.OriginalFileSize > int.MaxValue)
            throw new OfficialUpxMetadataException("Tamanho original ELF C_BASE invalido");

        var loads = metadata.OriginalProgramHeaders
            .Where(header => header.Type == 1)
            .ToArray();
        if (loads.Length == 0)
            throw new OfficialUpxMetadataException("ELF original sem PT_LOAD");

        byte[] result = new byte[(int)metadata.OriginalFileSize];
        var reader = new OfficialUpxExtentReader(data.ToArray(), metadata.Blocks!);

        foreach (OfficialElfProgramHeader load in loads)
        {
            ValidateOutputRange(load.Offset, load.FileSize, metadata.OriginalFileSize);
            byte[] extent = reader.ReadExtent(load.FileSize);
            extent.CopyTo(result, checked((int)load.Offset));
        }

        foreach (OfficialElfProgramHeader load in loads)
        {
            ulong high = checked(load.Offset + load.FileSize);
            ulong low = metadata.OriginalFileSize;
            foreach (OfficialElfProgramHeader candidate in loads)
            {
                if (candidate.Offset >= high && candidate.Offset < low)
                    low = candidate.Offset;
            }

            if (low < high)
                throw new OfficialUpxMetadataException("ELF original possui PT_LOAD sobrepostos");

            ulong gapSize = low - high;
            if (gapSize == 0)
                continue;

            ValidateOutputRange(high, gapSize, metadata.OriginalFileSize);
            byte[] gap = reader.ReadExtent(gapSize);
            gap.CopyTo(result, checked((int)high));
        }

        reader.EnsureFullyConsumed();
        return result;
    }

    private static void ValidateOutputRange(ulong offset, ulong size, uint originalFileSize)
    {
        if (offset > originalFileSize || size > originalFileSize - offset)
            throw new OfficialUpxMetadataException("Extent ELF original aponta para fora do arquivo");
    }

    private static ushort ReadOriginalElfMachine(ReadOnlySpan<byte> elf, bool bigEndian)
    {
        if (elf.Length < 20)
            throw new OfficialUpxMetadataException("ELF original descomprimido truncado");

        return ReadUInt16(elf[18..20], bigEndian);
    }

    private static ulong ReadOriginalElfEntry(ReadOnlySpan<byte> elf, bool bigEndian)
    {
        return elf[4] switch
        {
            1 when elf.Length >= 28 => ReadUInt32(elf[24..28], bigEndian),
            2 when elf.Length >= 32 => ReadUInt64(elf[24..32], bigEndian),
            _ => throw new OfficialUpxMetadataException("ELF original com entry point truncado")
        };
    }

    private static IReadOnlyList<OfficialElfProgramHeader> ReadOriginalProgramHeaders(ReadOnlySpan<byte> elf, bool bigEndian)
    {
        byte elfClass = elf[4];
        ulong phoff;
        ushort phentsize;
        ushort phnum;
        int minimumEntrySize;

        if (elfClass == 1)
        {
            if (elf.Length < 52)
                throw new OfficialUpxMetadataException("ELF32 original descomprimido truncado");

            phoff = ReadUInt32(elf[28..32], bigEndian);
            phentsize = ReadUInt16(elf[42..44], bigEndian);
            phnum = ReadUInt16(elf[44..46], bigEndian);
            minimumEntrySize = 32;
        }
        else if (elfClass == 2)
        {
            if (elf.Length < 64)
                throw new OfficialUpxMetadataException("ELF64 original descomprimido truncado");

            phoff = ReadUInt64(elf[32..40], bigEndian);
            phentsize = ReadUInt16(elf[54..56], bigEndian);
            phnum = ReadUInt16(elf[56..58], bigEndian);
            minimumEntrySize = 56;
        }
        else
        {
            throw new OfficialUpxMetadataException("ELF original com class invalido");
        }

        if (phnum == 0)
            throw new OfficialUpxMetadataException("ELF original sem program headers");
        if (phentsize < minimumEntrySize)
            throw new OfficialUpxMetadataException("ELF original com program header pequeno demais");
        if (phoff > (ulong)elf.Length || phoff + (ulong)phentsize * phnum > (ulong)elf.Length)
            throw new OfficialUpxMetadataException("ELF original com program headers truncados");

        var headers = new List<OfficialElfProgramHeader>(phnum);
        for (int i = 0; i < phnum; i++)
        {
            var span = elf.Slice((int)(phoff + (ulong)i * phentsize), phentsize);
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

    private static void ValidateExpectedOriginalElf(UpxFormat expectedFormat, byte elfClass, ushort machine)
    {
        (byte ExpectedClass, ushort ExpectedMachine)? expected = expectedFormat switch
        {
            UpxFormat.LinuxElfi386 => (1, 3),
            UpxFormat.LinuxElfAmd64 => (2, 62),
            UpxFormat.LinuxElfArmLE => (1, 40),
            UpxFormat.LinuxElfArm64LE => (2, 183),
            UpxFormat.LinuxElfMipsel => (1, 8),
            UpxFormat.LinuxElfMips => (1, 8),
            UpxFormat.LinuxElf32Ppc => (1, 20),
            UpxFormat.LinuxElf64Ppc => (2, 21),
            UpxFormat.LinuxElfRiscv64 => (2, 243),
            _ => null,
        };

        if (expected is null)
            return;

        if (elfClass != expected.Value.ExpectedClass || machine != expected.Value.ExpectedMachine)
        {
            throw new OfficialUpxMetadataException(
                $"ELF original divergente: esperado class={expected.Value.ExpectedClass}, machine={expected.Value.ExpectedMachine}; obtido class={elfClass}, machine={machine}");
        }
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> source, bool bigEndian)
        => bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(source) : BinaryPrimitives.ReadUInt16LittleEndian(source);

    private static uint ReadUInt32(ReadOnlySpan<byte> source, bool bigEndian)
        => bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(source) : BinaryPrimitives.ReadUInt32LittleEndian(source);

    private static ulong ReadUInt64(ReadOnlySpan<byte> source, bool bigEndian)
        => bigEndian ? BinaryPrimitives.ReadUInt64BigEndian(source) : BinaryPrimitives.ReadUInt64LittleEndian(source);
}
