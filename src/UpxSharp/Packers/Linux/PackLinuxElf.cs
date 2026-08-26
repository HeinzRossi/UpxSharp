using System.Buffers.Binary;
using System.Text;
using UpxSharp.Compression;
using UpxSharp.Core;
using UpxSharp.Filters;
using UpxSharp.IO;

namespace UpxSharp.Packers.Linux;

public abstract class PackLinuxElf : Packer
{
    protected const byte ELFCLASS32 = 1;
    protected const byte ELFCLASS64 = 2;
    protected const byte ELFDATA2LSB = 1;
    protected const byte ELFDATA2MSB = 2;

    protected const ushort EM_386 = 3;
    protected const ushort EM_MIPS = 8;
    protected const ushort EM_PPC = 20;
    protected const ushort EM_PPC64 = 21;
    protected const ushort EM_ARM = 40;
    protected const ushort EM_X86_64 = 62;
    protected const ushort EM_AARCH64 = 183;
    protected const ushort EM_RISCV = 243;

    protected const ulong SHF_EXECINSTR = 0x4;

    protected PackLinuxElf(InputFile file, Options options) : base(file, options) { }

    protected sealed record ElfHeaderInfo(
        byte Class,
        bool IsBigEndian,
        ushort Machine,
        ulong SectionTableOffset,
        ushort SectionHeaderSize,
        ushort SectionCount,
        ushort SectionNameStringTableIndex);

    protected sealed record ElfSectionHeader(
        uint NameOffset,
        uint Type,
        ulong Flags,
        ulong Offset,
        ulong Size,
        string Name);

    protected bool IsElfFile(byte expectedClass, ushort expectedMachine)
        => IsElfFile(expectedClass, expectedMachine, ELFDATA2LSB);

    protected bool IsElfFile(byte expectedClass, ushort expectedMachine, byte expectedData)
    {
        if (File.Length < 20) return false;

        Span<byte> header = stackalloc byte[20];
        File.ReadExact(header, 0);

        if (!header[..4].SequenceEqual("\u007FELF"u8)) return false;
        if (header[4] != expectedClass) return false;
        if (header[5] != expectedData) return false;

        bool isBigEndian = expectedData == ELFDATA2MSB;
        ushort machine = ReadUInt16(header[18..20], isBigEndian);
        return machine == expectedMachine;
    }

    protected ElfHeaderInfo ReadElfHeader(byte expectedClass, ushort expectedMachine)
        => ReadElfHeader(expectedClass, expectedMachine, ELFDATA2LSB);

    protected ElfHeaderInfo ReadElfHeader(byte expectedClass, ushort expectedMachine, byte expectedData)
    {
        if (File.Length < 64)
            throw new CantPackException("Arquivo ELF pequeno demais");

        Span<byte> ident = stackalloc byte[16];
        File.ReadExact(ident, 0);

        if (!ident[..4].SequenceEqual("\u007FELF"u8))
            throw new CantPackException("ELF magic inválido");
        if (ident[4] != expectedClass)
            throw new CantPackException("ELF class incorreto");
        if (ident[5] != expectedData)
            throw new CantPackException("ELF endianness incorreto");

        bool isBigEndian = expectedData == ELFDATA2MSB;

        if (expectedClass == ELFCLASS32)
        {
            Span<byte> header = stackalloc byte[52];
            File.ReadExact(header, 0);
            ushort machine = ReadUInt16(header[18..20], isBigEndian);
            if (machine != expectedMachine)
                throw new CantPackException("ELF machine incorreto");

            return new ElfHeaderInfo(
                expectedClass,
                isBigEndian,
                machine,
                ReadUInt32(header[32..36], isBigEndian),
                ReadUInt16(header[46..48], isBigEndian),
                ReadUInt16(header[48..50], isBigEndian),
                ReadUInt16(header[50..52], isBigEndian));
        }

        Span<byte> header64 = stackalloc byte[64];
        File.ReadExact(header64, 0);
        ushort machine64 = ReadUInt16(header64[18..20], isBigEndian);
        if (machine64 != expectedMachine)
            throw new CantPackException("ELF machine incorreto");

        return new ElfHeaderInfo(
            expectedClass,
            isBigEndian,
            machine64,
            ReadUInt64(header64[40..48], isBigEndian),
            ReadUInt16(header64[58..60], isBigEndian),
            ReadUInt16(header64[60..62], isBigEndian),
            ReadUInt16(header64[62..64], isBigEndian));
    }

    protected IReadOnlyList<ElfSectionHeader> ReadSectionHeaders(ReadOnlySpan<byte> data, ElfHeaderInfo header)
    {
        if (header.SectionTableOffset == 0 || header.SectionCount == 0)
            throw new CantPackException("ELF sem section table");

        var sections = new List<ElfSectionHeader>(header.SectionCount);
        for (int i = 0; i < header.SectionCount; i++)
        {
            ulong sectionOffset = header.SectionTableOffset + (ulong)i * header.SectionHeaderSize;
            if (sectionOffset > (ulong)data.Length || sectionOffset + header.SectionHeaderSize > (ulong)data.Length)
                throw new CantPackException("ELF section table truncada");

            var span = data.Slice((int)sectionOffset, header.SectionHeaderSize);
            if (header.Class == ELFCLASS32)
            {
                sections.Add(new ElfSectionHeader(
                    ReadUInt32(span[0..4], header.IsBigEndian),
                    ReadUInt32(span[4..8], header.IsBigEndian),
                    ReadUInt32(span[8..12], header.IsBigEndian),
                    ReadUInt32(span[16..20], header.IsBigEndian),
                    ReadUInt32(span[20..24], header.IsBigEndian),
                    string.Empty));
            }
            else
            {
                sections.Add(new ElfSectionHeader(
                    ReadUInt32(span[0..4], header.IsBigEndian),
                    ReadUInt32(span[4..8], header.IsBigEndian),
                    ReadUInt64(span[8..16], header.IsBigEndian),
                    ReadUInt64(span[24..32], header.IsBigEndian),
                    ReadUInt64(span[32..40], header.IsBigEndian),
                    string.Empty));
            }
        }

        if (header.SectionNameStringTableIndex >= sections.Count)
            return sections;

        var stringTable = sections[header.SectionNameStringTableIndex];
        if (stringTable.Offset > (ulong)data.Length || stringTable.Offset + stringTable.Size > (ulong)data.Length)
            throw new CantPackException("ELF section string table truncada");

        var names = data.Slice((int)stringTable.Offset, (int)stringTable.Size);
        var resolved = new List<ElfSectionHeader>(sections.Count);
        foreach (var section in sections)
            resolved.Add(section with { Name = ReadString(names, section.NameOffset) });

        return resolved;
    }

    protected ElfSectionHeader GetCodeSection(ReadOnlySpan<byte> data, ElfHeaderInfo header)
    {
        var sections = ReadSectionHeaders(data, header);
        var codeSection = sections.FirstOrDefault(section => section.Name == ".text")
            ?? sections.FirstOrDefault(section => (section.Flags & SHF_EXECINSTR) != 0);

        if (codeSection is null)
            throw new CantPackException("Seção de código ELF não encontrada");

        if (codeSection.Offset > (ulong)data.Length || codeSection.Offset + codeSection.Size > (ulong)data.Length)
            throw new CantPackException("Seção de código ELF truncada");

        return codeSection;
    }

    protected void DoPackElfFile(byte expectedClass, ushort expectedMachine, OutputFile output, Options options)
        => DoPackElfFile(expectedClass, expectedMachine, ELFDATA2LSB, output, options);

    protected void DoPackElfFile(byte expectedClass, ushort expectedMachine, byte expectedData, OutputFile output, Options options)
    {
        using var data = MemBuffer.Alloc((int)File.Length);
        File.ReadExact(data.Span, 0);

        var headerInfo = ReadElfHeader(expectedClass, expectedMachine, expectedData);
        var codeSection = GetCodeSection(data.Span, headerInfo);

        var sectionSpan = data.Span.Slice((int)codeSection.Offset, (int)codeSection.Size);
        var filterIds = FilterFactory.GetFiltersForArch(GetFormat());
        var levels = options.Level > 0 ? new[] { options.Level } : new[] { 1, 3, 5, 7, 9 };
        var result = CompressWithFilters(sectionSpan, filterIds, levels);

        Header = new PackHeader
        {
            Version = GetVersion(),
            Format = GetFormat(),
            Method = options.Method,
            Level = (byte)result.Level,
            UAdler = result.UAdler,
            CAdler = result.CAdler,
            ULen = (uint)sectionSpan.Length,
            CLen = (uint)result.CompressedSize,
            UFileSize = (uint)File.Length,
            FilterId = result.FilterId,
            FilterCto = result.FilterCto,
            NMru = result.NMru,
        };

        Span<byte> headerBuf = stackalloc byte[PackHeader.SerializedSize];
        Header.Write(headerBuf);

        output.Write(data.Span);
        output.Write(headerBuf);
        output.Write(result.CompressedData);
    }

    protected void DoUnpackElfFile(byte expectedClass, ushort expectedMachine, OutputFile output, Options options)
        => DoUnpackElfFile(expectedClass, expectedMachine, ELFDATA2LSB, output, options);

    protected void DoUnpackElfFile(byte expectedClass, ushort expectedMachine, byte expectedData, OutputFile output, Options options)
    {
        using var data = MemBuffer.Alloc((int)File.Length);
        File.ReadExact(data.Span, 0);

        if (!PackHeader.TryFindValidTrailer(data.Span, out int offset, out var header))
        {
            ThrowCorruptedScaffoldIfHeaderPresent(data.Span, "Payload ELF comprimido truncado");
            if (TryUnpackOfficialUpxIfDetected(data.Span, output))
                return;
            throw new NotPackedException();
        }
        if (header is null)
            throw new CorruptedPackException("PackHeader ELF ausente");

        int originalSize = offset;
        int compressedOffset = offset + PackHeader.SerializedSize;
        if (compressedOffset > data.Length || compressedOffset + header.CLen > data.Length)
            throw new CorruptedPackException("Payload ELF comprimido truncado");

        var compressedSpan = data.Span.Slice(compressedOffset, (int)header.CLen);
        Adler32.Verify(compressedSpan, header.CAdler, "payload comprimido ELF");

        var decompressed = new byte[header.ULen];
        var compressor = CompressorFactory.Create(header.Method);
        compressor.Decompress(compressedSpan, decompressed);

        using var exeBuffer = MemBuffer.Alloc(originalSize);
        data.Span.Slice(0, originalSize).CopyTo(exeBuffer.Span);

        var headerInfo = ReadElfHeader(expectedClass, expectedMachine, expectedData);
        var codeSection = GetCodeSection(exeBuffer.Span, headerInfo);
        if (header.ULen > codeSection.Size)
            throw new CorruptedPackException("Payload ELF descomprimido excede seção de código");

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

        Adler32.Verify(decompressed, header.UAdler, "payload descomprimido ELF");
        decompressed.AsSpan().CopyTo(exeBuffer.Span.Slice((int)codeSection.Offset, (int)header.ULen));

        output.Write(exeBuffer.Span);
    }

    protected bool IsAlreadyPacked() => HasPackHeader();

    protected bool HasPackHeader()
    {
        return HasScaffoldPackHeader();
    }

    protected bool HasOfficialPackHeader() => HasOfficialUpx();

    private static string ReadString(ReadOnlySpan<byte> table, uint offset)
    {
        if (offset >= table.Length)
            return string.Empty;

        int end = (int)offset;
        while (end < table.Length && table[end] != 0)
            end++;

        return Encoding.ASCII.GetString(table[(int)offset..end]);
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> source, bool isBigEndian)
        => isBigEndian
            ? BinaryPrimitives.ReadUInt16BigEndian(source)
            : BinaryPrimitives.ReadUInt16LittleEndian(source);

    private static uint ReadUInt32(ReadOnlySpan<byte> source, bool isBigEndian)
        => isBigEndian
            ? BinaryPrimitives.ReadUInt32BigEndian(source)
            : BinaryPrimitives.ReadUInt32LittleEndian(source);

    private static ulong ReadUInt64(ReadOnlySpan<byte> source, bool isBigEndian)
        => isBigEndian
            ? BinaryPrimitives.ReadUInt64BigEndian(source)
            : BinaryPrimitives.ReadUInt64LittleEndian(source);
}
