using System.Buffers.Binary;
using UpxSharp.Compression;
using UpxSharp.Core;
using UpxSharp.Filters;
using UpxSharp.IO;

namespace UpxSharp.Packers.Legacy;

public abstract class LegacyScaffoldPacker : Packer
{
    protected LegacyScaffoldPacker(InputFile file, Options options) : base(file, options) { }

    protected sealed record PayloadInfo(uint Offset, uint Size);

    protected abstract bool IsLegacyFile();
    protected abstract PayloadInfo ReadPayloadInfo();

    public override bool? CanPack()
    {
        if (!IsLegacyFile()) return false;
        if (HasPackHeader()) return null;

        try
        {
            _ = ReadPayloadInfo();
            return true;
        }
        catch (Exception ex) when (ex is CantPackException or EndOfStreamException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    public override bool? CanUnpack()
    {
        if (!IsLegacyFile()) return false;
        return HasPackHeader() || HasOfficialPackHeader();
    }

    public override void DoPack(OutputFile output, Options options)
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

    public override void DoUnpack(OutputFile output, Options options)
    {
        using var data = MemBuffer.Alloc((int)File.Length);
        File.ReadExact(data.Span, 0);

        if (!PackHeader.TryFindValidTrailer(data.Span, out int offset, out var header))
        {
            ThrowCorruptedScaffoldIfHeaderPresent(data.Span, "Payload legado comprimido truncado");
            if (TryUnpackOfficialUpxIfDetected(data.Span, output))
                return;
            throw new NotPackedException();
        }
        if (header is null)
            throw new CorruptedPackException("PackHeader legado ausente");

        int originalSize = offset;
        int compressedOffset = offset + PackHeader.SerializedSize;
        if (compressedOffset > data.Length || compressedOffset + header.CLen > data.Length)
            throw new CorruptedPackException("Payload legado comprimido truncado");

        var compressedSpan = data.Span.Slice(compressedOffset, (int)header.CLen);
        Adler32.Verify(compressedSpan, header.CAdler, "payload comprimido legado");

        var decompressed = new byte[header.ULen];
        var compressor = CompressorFactory.Create(header.Method);
        compressor.Decompress(compressedSpan, decompressed);

        using var exeBuffer = MemBuffer.Alloc(originalSize);
        data.Span.Slice(0, originalSize).CopyTo(exeBuffer.Span);

        var payload = ReadPayloadInfo();
        if (header.ULen > payload.Size)
            throw new CorruptedPackException("Payload legado descomprimido excede área original");

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

        Adler32.Verify(decompressed, header.UAdler, "payload descomprimido legado");
        decompressed.AsSpan().CopyTo(exeBuffer.Span.Slice((int)payload.Offset, (int)header.ULen));

        output.Write(exeBuffer.Span);
    }

    protected bool HasPackHeader()
    {
        return HasScaffoldPackHeader();
    }

    protected bool HasOfficialPackHeader() => HasOfficialUpx();

    protected PayloadInfo ReadOffsetSizeFields(int offsetField, int sizeField, int minimumOffset)
    {
        Span<byte> fields = stackalloc byte[4];
        File.ReadExact(fields, offsetField);
        uint payloadOffset = BinaryPrimitives.ReadUInt32LittleEndian(fields);
        File.ReadExact(fields, sizeField);
        uint payloadSize = BinaryPrimitives.ReadUInt32LittleEndian(fields);

        if (payloadOffset < minimumOffset || payloadSize == 0)
            throw new CantPackException("Payload legado scaffold inválido");

        if (payloadOffset > File.Length || payloadOffset + payloadSize > File.Length)
            throw new CantPackException("Payload legado scaffold truncado");

        return new PayloadInfo(payloadOffset, payloadSize);
    }
}
