using System.Buffers.Binary;
using System.Text;
using UpxSharp.Compression;
using UpxSharp.Core;
using UpxSharp.Filters;
using UpxSharp.IO;

namespace UpxSharp.Packers.MacOs;

public abstract class PackMachBase : Packer
{
    protected const uint MH_MAGIC = 0xFEEDFACE;
    protected const uint MH_MAGIC_64 = 0xFEEDFACF;

    protected const int CPU_TYPE_I386 = 7;
    protected const int CPU_TYPE_X86_64 = 0x01000007;

    protected const uint LC_SEGMENT = 0x1;
    protected const uint LC_SEGMENT_64 = 0x19;
    protected const uint S_ATTR_PURE_INSTRUCTIONS = 0x80000000;
    protected const uint S_ATTR_SOME_INSTRUCTIONS = 0x00000400;

    protected PackMachBase(InputFile file, Options options) : base(file, options) { }

    protected sealed record MachHeaderInfo(
        bool Is64,
        int CpuType,
        uint LoadCommandCount,
        uint LoadCommandSize,
        int LoadCommandOffset);

    protected sealed record MachSectionInfo(
        string SegmentName,
        string SectionName,
        ulong Address,
        ulong Size,
        uint Offset,
        uint Flags);

    protected bool IsMachFile(uint expectedMagic, int expectedCpuType)
    {
        if (File.Length < 28) return false;

        Span<byte> header = stackalloc byte[8];
        File.ReadExact(header, 0);

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (magic != expectedMagic) return false;

        int cpuType = BinaryPrimitives.ReadInt32LittleEndian(header[4..8]);
        return cpuType == expectedCpuType;
    }

    protected MachHeaderInfo ReadMachHeader(uint expectedMagic, int expectedCpuType)
    {
        Span<byte> header = stackalloc byte[32];
        File.ReadExact(header, 0);

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (magic != expectedMagic)
            throw new CantPackException("Mach-O magic inválido");

        int cpuType = BinaryPrimitives.ReadInt32LittleEndian(header[4..8]);
        if (cpuType != expectedCpuType)
            throw new CantPackException("Mach-O CPU type incorreto");

        bool is64 = magic == MH_MAGIC_64;
        return new MachHeaderInfo(
            is64,
            cpuType,
            BinaryPrimitives.ReadUInt32LittleEndian(header[16..20]),
            BinaryPrimitives.ReadUInt32LittleEndian(header[20..24]),
            is64 ? 32 : 28);
    }

    protected IReadOnlyList<MachSectionInfo> ReadSections(ReadOnlySpan<byte> data, MachHeaderInfo header)
    {
        var sections = new List<MachSectionInfo>();
        int commandOffset = header.LoadCommandOffset;

        for (uint i = 0; i < header.LoadCommandCount; i++)
        {
            if (commandOffset < 0 || commandOffset + 8 > data.Length)
                throw new CantPackException("Mach-O load command truncado");

            uint command = BinaryPrimitives.ReadUInt32LittleEndian(data[commandOffset..]);
            uint commandSize = BinaryPrimitives.ReadUInt32LittleEndian(data[(commandOffset + 4)..]);
            if (commandSize < 8 || commandOffset + commandSize > data.Length)
                throw new CantPackException("Mach-O load command inválido");

            if (command == LC_SEGMENT && commandSize >= 56)
                ReadSegment32(data.Slice(commandOffset, (int)commandSize), sections);
            else if (command == LC_SEGMENT_64 && commandSize >= 72)
                ReadSegment64(data.Slice(commandOffset, (int)commandSize), sections);

            commandOffset += (int)commandSize;
        }

        return sections;
    }

    protected MachSectionInfo GetCodeSection(ReadOnlySpan<byte> data, MachHeaderInfo header)
    {
        var sections = ReadSections(data, header);
        var codeSection = sections.FirstOrDefault(section =>
                section.SegmentName == "__TEXT" && section.SectionName == "__text")
            ?? sections.FirstOrDefault(section =>
                (section.Flags & (S_ATTR_PURE_INSTRUCTIONS | S_ATTR_SOME_INSTRUCTIONS)) != 0);

        if (codeSection is null)
            throw new CantPackException("Seção de código Mach-O não encontrada");

        if (codeSection.Offset > data.Length || codeSection.Offset + codeSection.Size > (ulong)data.Length)
            throw new CantPackException("Seção de código Mach-O truncada");

        return codeSection;
    }

    protected void DoPackMachFile(uint expectedMagic, int expectedCpuType, OutputFile output, Options options)
    {
        using var data = MemBuffer.Alloc((int)File.Length);
        File.ReadExact(data.Span, 0);

        var headerInfo = ReadMachHeader(expectedMagic, expectedCpuType);
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

    protected void DoUnpackMachFile(uint expectedMagic, int expectedCpuType, OutputFile output, Options options)
    {
        using var data = MemBuffer.Alloc((int)File.Length);
        File.ReadExact(data.Span, 0);

        if (!PackHeader.TryFindValidTrailer(data.Span, out int offset, out var header))
        {
            ThrowCorruptedScaffoldIfHeaderPresent(data.Span, "Payload Mach-O comprimido truncado");
            if (TryUnpackOfficialUpxIfDetected(data.Span, output))
                return;
            throw new NotPackedException();
        }
        if (header is null)
            throw new CorruptedPackException("PackHeader Mach-O ausente");

        int originalSize = offset;
        int compressedOffset = offset + PackHeader.SerializedSize;
        if (compressedOffset > data.Length || compressedOffset + header.CLen > data.Length)
            throw new CorruptedPackException("Payload Mach-O comprimido truncado");

        var compressedSpan = data.Span.Slice(compressedOffset, (int)header.CLen);
        Adler32.Verify(compressedSpan, header.CAdler, "payload comprimido Mach-O");

        var decompressed = new byte[header.ULen];
        var compressor = CompressorFactory.Create(header.Method);
        compressor.Decompress(compressedSpan, decompressed);

        using var exeBuffer = MemBuffer.Alloc(originalSize);
        data.Span.Slice(0, originalSize).CopyTo(exeBuffer.Span);

        var headerInfo = ReadMachHeader(expectedMagic, expectedCpuType);
        var codeSection = GetCodeSection(exeBuffer.Span, headerInfo);
        if (header.ULen > codeSection.Size)
            throw new CorruptedPackException("Payload Mach-O descomprimido excede seção de código");

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

        Adler32.Verify(decompressed, header.UAdler, "payload descomprimido Mach-O");
        decompressed.AsSpan().CopyTo(exeBuffer.Span.Slice((int)codeSection.Offset, (int)header.ULen));

        output.Write(exeBuffer.Span);
    }

    protected bool IsAlreadyPacked() => HasPackHeader();

    protected bool HasPackHeader()
    {
        return HasScaffoldPackHeader();
    }

    protected bool HasOfficialPackHeader() => HasOfficialUpx();

    private static void ReadSegment32(ReadOnlySpan<byte> command, List<MachSectionInfo> sections)
    {
        string segmentName = ReadFixedString(command[8..24]);
        uint sectionCount = BinaryPrimitives.ReadUInt32LittleEndian(command[48..52]);
        int sectionOffset = 56;

        for (uint i = 0; i < sectionCount; i++)
        {
            if (sectionOffset + 68 > command.Length)
                throw new CantPackException("Mach-O section truncada");

            var section = command.Slice(sectionOffset, 68);
            sections.Add(new MachSectionInfo(
                segmentName,
                ReadFixedString(section[0..16]),
                BinaryPrimitives.ReadUInt32LittleEndian(section[32..36]),
                BinaryPrimitives.ReadUInt32LittleEndian(section[36..40]),
                BinaryPrimitives.ReadUInt32LittleEndian(section[40..44]),
                BinaryPrimitives.ReadUInt32LittleEndian(section[56..60])));

            sectionOffset += 68;
        }
    }

    private static void ReadSegment64(ReadOnlySpan<byte> command, List<MachSectionInfo> sections)
    {
        string segmentName = ReadFixedString(command[8..24]);
        uint sectionCount = BinaryPrimitives.ReadUInt32LittleEndian(command[64..68]);
        int sectionOffset = 72;

        for (uint i = 0; i < sectionCount; i++)
        {
            if (sectionOffset + 80 > command.Length)
                throw new CantPackException("Mach-O section64 truncada");

            var section = command.Slice(sectionOffset, 80);
            sections.Add(new MachSectionInfo(
                segmentName,
                ReadFixedString(section[0..16]),
                BinaryPrimitives.ReadUInt64LittleEndian(section[32..40]),
                BinaryPrimitives.ReadUInt64LittleEndian(section[40..48]),
                BinaryPrimitives.ReadUInt32LittleEndian(section[48..52]),
                BinaryPrimitives.ReadUInt32LittleEndian(section[64..68])));

            sectionOffset += 80;
        }
    }

    private static string ReadFixedString(ReadOnlySpan<byte> value)
    {
        int end = 0;
        while (end < value.Length && value[end] != 0)
            end++;

        return Encoding.ASCII.GetString(value[..end]);
    }
}
