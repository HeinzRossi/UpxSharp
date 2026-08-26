using System.Buffers.Binary;
using UpxSharp.Compression;
using UpxSharp.Core;
using UpxSharp.Filters;
using UpxSharp.IO;

namespace UpxSharp.Packers.Legacy;

public sealed class PackVmlinuzI386 : Packer
{
    private const int SetupSignatureOffset = 0x202;
    private const int PayloadOffsetField = 0x210;
    private const int PayloadSizeField = 0x214;
    private const int ScaffoldHeaderSize = 0x218;
    private const int SetupSectorsOffset = 0x1F1;
    private const int DefaultSetupSectors = 4;

    public PackVmlinuzI386(InputFile file, Options options) : base(file, options) { }

    public override UpxFormat GetFormat() => UpxFormat.Vmlinuzi386;
    public override string GetName() => "vmlinuz/i386";
    public override byte GetVersion() => 60;
    public override bool IsBigEndian => false;

    public override bool? CanPack()
        => IsVmlinuzFile()
            ? IsAlreadyPacked() ? null : true
            : false;

    public override bool? CanUnpack()
    {
        if (!IsVmlinuzFile()) return false;
        return HasPackHeader() || HasOfficialPackHeader();
    }

    public override void DoPack(OutputFile output, Options options)
        => DoPackVmlinuz(output, options);

    public override void DoUnpack(OutputFile output, Options options)
        => DoUnpackVmlinuz(output, options);

    private bool IsVmlinuzFile()
    {
        if (File.Length < ScaffoldHeaderSize) return false;

        Span<byte> header = stackalloc byte[ScaffoldHeaderSize];
        File.ReadExact(header, 0);

        return header[0] == (byte)'M'
            && header[1] == (byte)'Z'
            && header.Slice(SetupSignatureOffset, 4).SequenceEqual("HdrS"u8);
    }

    private VmlinuzPayloadInfo ReadPayloadInfo()
    {
        if (!IsVmlinuzFile())
            throw new CantPackException("vmlinuz i386 scaffold inválido");

        Span<byte> fields = stackalloc byte[8];
        File.ReadExact(fields, PayloadOffsetField);

        uint payloadOffset = BinaryPrimitives.ReadUInt32LittleEndian(fields[..4]);
        uint payloadSize = BinaryPrimitives.ReadUInt32LittleEndian(fields[4..8]);

        if (payloadOffset < ScaffoldHeaderSize || payloadSize == 0)
            return ReadBootProtocolPayloadInfo();

        if (payloadOffset > File.Length || payloadOffset + payloadSize > File.Length)
            return ReadBootProtocolPayloadInfo();

        return new VmlinuzPayloadInfo(payloadOffset, payloadSize);
    }

    private VmlinuzPayloadInfo ReadBootProtocolPayloadInfo()
    {
        Span<byte> setupSectorsBuffer = stackalloc byte[1];
        File.ReadExact(setupSectorsBuffer, SetupSectorsOffset);

        byte setupSectors = setupSectorsBuffer[0];
        uint payloadOffset = checked((uint)((setupSectors == 0 ? DefaultSetupSectors : setupSectors) + 1) * 512u);
        payloadOffset = Math.Max(payloadOffset, (uint)ScaffoldHeaderSize);
        if (payloadOffset >= File.Length)
            throw new CantPackException("Payload vmlinuz boot protocol vazio");

        return new VmlinuzPayloadInfo(payloadOffset, checked((uint)File.Length - payloadOffset));
    }

    private void DoPackVmlinuz(OutputFile output, Options options)
    {
        using var data = MemBuffer.Alloc((int)File.Length);
        File.ReadExact(data.Span, 0);

        var payload = ReadPayloadInfo();
        var payloadSpan = data.Span.Slice((int)payload.Offset, (int)payload.Size);

        var filterIds = FilterFactory.GetFiltersForArch(GetFormat());
        var levels = options.Level > 0 ? new[] { options.Level } : new[] { 1, 3, 5, 7, 9 };
        var result = CompressWithFilters(payloadSpan, filterIds, levels);

        Header = new PackHeader
        {
            Version = GetVersion(),
            Format = GetFormat(),
            Method = options.Method,
            Level = (byte)result.Level,
            UAdler = result.UAdler,
            CAdler = result.CAdler,
            ULen = (uint)payloadSpan.Length,
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

    private void DoUnpackVmlinuz(OutputFile output, Options options)
    {
        using var data = MemBuffer.Alloc((int)File.Length);
        File.ReadExact(data.Span, 0);

        if (!PackHeader.TryFindValidTrailer(data.Span, out int offset, out var header))
        {
            ThrowCorruptedScaffoldIfHeaderPresent(data.Span, "Payload vmlinuz comprimido truncado");
            if (TryUnpackOfficialUpxIfDetected(data.Span, output))
                return;
            throw new NotPackedException();
        }
        if (header is null)
            throw new CorruptedPackException("PackHeader vmlinuz ausente");

        int originalSize = offset;
        int compressedOffset = offset + PackHeader.SerializedSize;
        if (compressedOffset > data.Length || compressedOffset + header.CLen > data.Length)
            throw new CorruptedPackException("Payload vmlinuz comprimido truncado");

        var compressedSpan = data.Span.Slice(compressedOffset, (int)header.CLen);
        Adler32.Verify(compressedSpan, header.CAdler, "payload comprimido vmlinuz");

        var decompressed = new byte[header.ULen];
        var compressor = CompressorFactory.Create(header.Method);
        compressor.Decompress(compressedSpan, decompressed);

        using var exeBuffer = MemBuffer.Alloc(originalSize);
        data.Span.Slice(0, originalSize).CopyTo(exeBuffer.Span);

        var payload = ReadPayloadInfo();
        if (header.ULen > payload.Size)
            throw new CorruptedPackException("Payload vmlinuz descomprimido excede área original");

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

        Adler32.Verify(decompressed, header.UAdler, "payload descomprimido vmlinuz");
        decompressed.AsSpan().CopyTo(exeBuffer.Span.Slice((int)payload.Offset, (int)header.ULen));

        output.Write(exeBuffer.Span);
    }

    private bool IsAlreadyPacked() => HasPackHeader();

    private bool HasPackHeader()
    {
        return HasScaffoldPackHeader();
    }

    private bool HasOfficialPackHeader() => HasOfficialUpx();

    private sealed record VmlinuzPayloadInfo(uint Offset, uint Size);
}
