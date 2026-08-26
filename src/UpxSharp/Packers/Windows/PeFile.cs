using System.Buffers.Binary;
using System.Text;
using UpxSharp.Compression;
using UpxSharp.Core;
using UpxSharp.Filters;
using UpxSharp.IO;
using UpxSharp.Stubs;

namespace UpxSharp.Packers.Windows;

public abstract class PeFile : Packer
{
    protected static ReadOnlySpan<byte> MzMagic => "MZ"u8;
    protected static ReadOnlySpan<byte> PeMagic => "PE\0\0"u8;

    protected const ushort IMAGE_FILE_MACHINE_I386 = 0x014C;
    protected const ushort IMAGE_FILE_MACHINE_AMD64 = 0x8664;
    protected const ushort IMAGE_FILE_MACHINE_ARM64 = 0xAA64;

    protected const uint IMAGE_SCN_MEM_READ = 0x40000000;
    protected const uint IMAGE_SCN_MEM_WRITE = 0x80000000;
    protected const uint IMAGE_SCN_MEM_EXECUTE = 0x20000000;
    protected const uint IMAGE_SCN_CNT_CODE = 0x00000020;

    protected PeFile(InputFile file, Options options) : base(file, options) { }

    protected sealed record PeHeaderInfo(
        int PeOffset,
        ushort Machine,
        ushort SectionCount,
        ushort OptionalHeaderSize,
        int SectionTableOffset,
        uint AddressOfEntryPoint,
        ulong ImageBase,
        uint SizeOfHeaders);

    protected sealed record PeSectionHeader(
        string Name,
        uint VirtualSize,
        uint VirtualAddress,
        uint SizeOfRawData,
        uint PointerToRawData,
        uint Characteristics);

    protected int ReadPeOffset()
    {
        Span<byte> buf = stackalloc byte[64];
        File.ReadExact(buf, 0);

        if (!buf[..2].SequenceEqual(MzMagic))
            throw new CantPackException("Não é um arquivo MZ/PE válido");

        return (int)BinaryPrimitives.ReadUInt32LittleEndian(buf[60..]);
    }

    protected bool IsPeFile(ushort expectedMachine)
    {
        if (File.Length < 256) return false;

        Span<byte> buf = stackalloc byte[4];
        File.ReadExact(buf, 0);
        if (!buf[..2].SequenceEqual(MzMagic)) return false;

        int peOffset = ReadPeOffset();
        if (peOffset < 64 || peOffset + 6 > File.Length)
            return false;

        Span<byte> peHdr = stackalloc byte[6];
        File.ReadExact(peHdr, peOffset);

        if (!peHdr[..4].SequenceEqual(PeMagic)) return false;

        ushort machine = BinaryPrimitives.ReadUInt16LittleEndian(peHdr[4..]);
        return machine == expectedMachine;
    }

    protected PeHeaderInfo ReadPeImageHeaders(ushort expectedMachine)
    {
        int peOffset = ReadPeOffset();

        Span<byte> fileHeader = stackalloc byte[24];
        File.ReadExact(fileHeader, peOffset);

        if (!fileHeader[..4].SequenceEqual(PeMagic))
            throw new CantPackException("PE header inválido");

        ushort machine = BinaryPrimitives.ReadUInt16LittleEndian(fileHeader[4..6]);
        if (machine != expectedMachine)
            throw new CantPackException("Arquivo PE com máquina incorreta");

        ushort sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(fileHeader[6..8]);
        ushort optionalHeaderSize = BinaryPrimitives.ReadUInt16LittleEndian(fileHeader[20..22]);
        int optionalHeaderOffset = peOffset + 24;
        int sectionTableOffset = optionalHeaderOffset + optionalHeaderSize;

        byte[] optionalHeader = new byte[Math.Max((int)optionalHeaderSize, 64)];
        File.ReadExact(optionalHeader, optionalHeaderOffset);

        ushort optionalMagic = BinaryPrimitives.ReadUInt16LittleEndian(optionalHeader.AsSpan(0, 2));
        uint addressOfEntryPoint = BinaryPrimitives.ReadUInt32LittleEndian(optionalHeader.AsSpan(16, 4));
        uint sizeOfHeaders = BinaryPrimitives.ReadUInt32LittleEndian(optionalHeader.AsSpan(60, 4));
        ulong imageBase;

        if (optionalMagic == 0x10b)
            imageBase = BinaryPrimitives.ReadUInt32LittleEndian(optionalHeader.AsSpan(28, 4));
        else if (optionalMagic == 0x20b)
            imageBase = BinaryPrimitives.ReadUInt64LittleEndian(optionalHeader.AsSpan(24, 8));
        else
            throw new CantPackException($"Optional header magic desconhecido: 0x{optionalMagic:X4}");

        return new PeHeaderInfo(
            peOffset,
            machine,
            sectionCount,
            optionalHeaderSize,
            sectionTableOffset,
            addressOfEntryPoint,
            imageBase,
            sizeOfHeaders);
    }

    protected IReadOnlyList<PeSectionHeader> ReadSectionHeaders(PeHeaderInfo header)
    {
        var sections = new List<PeSectionHeader>(header.SectionCount);
        byte[] sectionData = new byte[40];

        for (int i = 0; i < header.SectionCount; i++)
        {
            int offset = header.SectionTableOffset + i * 40;
            File.ReadExact(sectionData, offset);

            string name = Encoding.ASCII.GetString(sectionData, 0, 8).TrimEnd('\0');
            uint virtualSize = BinaryPrimitives.ReadUInt32LittleEndian(sectionData.AsSpan(8, 4));
            uint virtualAddress = BinaryPrimitives.ReadUInt32LittleEndian(sectionData.AsSpan(12, 4));
            uint sizeOfRawData = BinaryPrimitives.ReadUInt32LittleEndian(sectionData.AsSpan(16, 4));
            uint pointerToRawData = BinaryPrimitives.ReadUInt32LittleEndian(sectionData.AsSpan(20, 4));
            uint characteristics = BinaryPrimitives.ReadUInt32LittleEndian(sectionData.AsSpan(36, 4));

            sections.Add(new PeSectionHeader(
                name,
                virtualSize,
                virtualAddress,
                sizeOfRawData,
                pointerToRawData,
                characteristics));
        }

        return sections;
    }

    protected PeSectionHeader? GetSection(string name, PeHeaderInfo header)
    {
        return ReadSectionHeaders(header).FirstOrDefault(section => section.Name == name);
    }

    protected PeSectionHeader GetCodeSection(PeHeaderInfo header)
    {
        var sections = ReadSectionHeaders(header);
        var codeSection = sections.FirstOrDefault(s => s.Name == ".text")
            ?? sections.FirstOrDefault(s => (s.Characteristics & IMAGE_SCN_CNT_CODE) != 0);

        return codeSection ?? throw new CantPackException("Seção de código não encontrada");
    }

    protected void DoPackPeFile(ushort expectedMachine, OutputFile output, Options options)
    {
        using var data = MemBuffer.Alloc((int)File.Length);
        File.ReadExact(data.Span, 0);

        _ = ReadPeImageHeaders(expectedMachine);

        var levels = options.Level > 0 ? new[] { options.Level } : new[] { 1, 3, 5, 7, 9 };
        var result = CompressFullImage(data.Span, levels);
        byte[] stub = CreatePeStubShell(expectedMachine, StubRepository.Load(GetFormat(), requireReal: true));
        long packedSize = (long)stub.Length + PackHeader.SerializedSize + result.CompressedSize;
        if (packedSize >= File.Length)
            throw new CantPackException("resultado não ficou menor; arquivo não foi gerado.");

        Header = new PackHeader
        {
            Version = GetVersion(),
            Format = GetFormat(),
            Method = options.Method,
            Level = (byte)result.Level,
            UAdler = result.UAdler,
            CAdler = result.CAdler,
            ULen = (uint)data.Length,
            CLen = (uint)result.CompressedSize,
            UFileSize = (uint)stub.Length,
            FilterId = 0,
            FilterCto = 0,
            NMru = 0,
            PayloadKind = PackPayloadKind.FullImage,
        };

        Span<byte> headerBuf = stackalloc byte[PackHeader.SerializedSize];
        Header.Write(headerBuf);

        output.Write(stub);
        output.Write(headerBuf);
        output.Write(result.CompressedData);
    }

    protected void DoUnpackPeFile(ushort expectedMachine, OutputFile output, Options options)
    {
        using var data = MemBuffer.Alloc((int)File.Length);
        File.ReadExact(data.Span, 0);

        if (!PackHeader.TryFindValidTrailer(data.Span, out int offset, out var header))
        {
            ThrowCorruptedScaffoldIfHeaderPresent(data.Span, "Payload PE comprimido truncado");
            if (TryUnpackOfficialUpxIfDetected(data.Span, output))
                return;
            throw new NotPackedException();
        }
        if (header is null)
            throw new CorruptedPackException("PackHeader ausente");

        int originalSize = offset;
        int compressedOffset = offset + PackHeader.SerializedSize;
        if (compressedOffset > data.Length || compressedOffset + header.CLen > data.Length)
            throw new CorruptedPackException("Payload comprimido truncado");

        var compressedSpan = data.Span.Slice(compressedOffset, (int)header.CLen);
        Adler32.Verify(compressedSpan, header.CAdler, "payload comprimido PE");

        var decompressed = new byte[header.ULen];

        var compressor = CompressorFactory.Create(header.Method);
        compressor.Decompress(compressedSpan, decompressed);

        if (header.PayloadKind == PackPayloadKind.FullImage)
        {
            Adler32.Verify(decompressed, header.UAdler, "imagem PE descomprimida");
            output.Write(decompressed);
            return;
        }

        using var exeBuffer = MemBuffer.Alloc(originalSize);
        data.Span.Slice(0, originalSize).CopyTo(exeBuffer.Span);

        var headerInfo = ReadPeImageHeaders(expectedMachine);
        var codeSection = GetCodeSection(headerInfo);

        if (header.ULen > codeSection.SizeOfRawData)
            throw new CorruptedPackException("Payload descomprimido excede seção de código");

        var filter = FilterFactory.Create(header.FilterId);
        if (filter is not null)
        {
            var filterState = new FilterState
            {
                Id = header.FilterId,
                Buffer = decompressed,
                BufLen = decompressed.Length,
                Cto = header.FilterCto,
                NMru = header.NMru,
            };
            filter.Unfilter(filterState);
        }

        Adler32.Verify(decompressed, header.UAdler, "payload descomprimido PE");

        decompressed.AsSpan().CopyTo(exeBuffer.Span.Slice((int)codeSection.PointerToRawData, (int)header.ULen));

        output.Write(exeBuffer.Span);
    }

    private CompressResult CompressFullImage(ReadOnlySpan<byte> image, IEnumerable<int> levels)
    {
        var best = new CompressResult { CompressedSize = int.MaxValue };
        byte[] original = image.ToArray();
        var compressor = CompressorFactory.Create(Options.Method);

        foreach (int level in levels)
        {
            byte[] destination = new byte[GetFullImageCompressionBufferSize(original.Length)];
            int cLen = compressor.Compress(original, destination, level);
            if (cLen < 0 || cLen >= best.CompressedSize)
                continue;

            best = new CompressResult
            {
                Level = level,
                CompressedSize = cLen,
                CompressedData = destination[..cLen],
                UAdler = Adler32.Compute(original),
                CAdler = Adler32.Compute(destination.AsSpan(0, cLen)),
            };
        }

        if (best.CompressedSize == int.MaxValue)
            throw new CantPackException("Dados não foram compressíveis com nenhuma combinação.");

        return best;
    }

    private static int GetFullImageCompressionBufferSize(int sourceLength)
        => checked(sourceLength + Math.Max(65536, sourceLength / 4 + 1024));

    private static byte[] CreatePeStubShell(ushort machine, byte[] loaderStub)
    {
        bool is64 = machine == IMAGE_FILE_MACHINE_AMD64;
        const int peOffset = 0x80;
        const int sizeOfHeaders = 0x200;
        const int sectionRawOffset = 0x200;
        const uint sectionRva = 0x1000;
        const uint fileAlignment = 0x200;
        const uint sectionAlignment = 0x1000;
        ushort optionalHeaderSize = is64 ? (ushort)0xF0 : (ushort)0xE0;
        uint sectionRawSize = Align((uint)loaderStub.Length, fileAlignment);
        uint sizeOfImage = Align(sectionRva + Align((uint)loaderStub.Length, sectionAlignment), sectionAlignment);

        byte[] image = new byte[checked(sectionRawOffset + (int)sectionRawSize)];
        image[0] = (byte)'M';
        image[1] = (byte)'Z';
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(60), peOffset);

        image[peOffset] = (byte)'P';
        image[peOffset + 1] = (byte)'E';
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(peOffset + 4), machine);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(peOffset + 6), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(peOffset + 20), optionalHeaderSize);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(peOffset + 22), 0x010F);

        int optional = peOffset + 24;
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(optional), is64 ? (ushort)0x20B : (ushort)0x10B);
        image[optional + 2] = 14;
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(optional + 4), sectionRawSize);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(optional + 16), sectionRva);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(optional + 20), sectionRva);
        if (is64)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(image.AsSpan(optional + 24), 0x140000000);
            BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(optional + 108), 16);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(optional + 24), sectionRva);
            BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(optional + 28), 0x400000);
            BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(optional + 92), 16);
        }

        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(optional + 32), sectionAlignment);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(optional + 36), fileAlignment);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(optional + 40), 6);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(optional + 48), 6);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(optional + 56), sizeOfImage);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(optional + 60), sizeOfHeaders);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(optional + 68), 3);

        int section = optional + optionalHeaderSize;
        Encoding.ASCII.GetBytes(".upx\0\0\0\0").CopyTo(image.AsSpan(section, 8));
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(section + 8), (uint)loaderStub.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(section + 12), sectionRva);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(section + 16), sectionRawSize);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(section + 20), sectionRawOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(section + 36), IMAGE_SCN_CNT_CODE | IMAGE_SCN_MEM_EXECUTE | IMAGE_SCN_MEM_READ);
        loaderStub.CopyTo(image.AsSpan(sectionRawOffset));
        return image;
    }

    private static uint Align(uint value, uint alignment)
        => (value + alignment - 1) / alignment * alignment;

    protected bool IsAlreadyPacked()
    {
        return HasPackHeader();
    }

    protected bool HasPackHeader()
    {
        return HasScaffoldPackHeader();
    }

    protected bool HasOfficialPackHeader() => HasOfficialUpx();
}
