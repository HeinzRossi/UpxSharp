using System.Buffers.Binary;
using UpxSharp.Core;

namespace UpxSharp.Official;

public sealed record OfficialPackHeader(
    int Offset,
    int Size,
    byte Version,
    byte Format,
    CompressionMethod Method,
    byte Level,
    uint UncompressedAdler,
    uint CompressedAdler,
    uint UncompressedLength,
    uint CompressedLength,
    uint OriginalFileSize,
    byte FilterId,
    byte FilterCto,
    byte NMru);

public static class OfficialPackHeaderDecoder
{
    private const byte DosComFormat = 1;
    private const byte DosSysFormat = 2;
    private const byte DosExeFormat = 3;
    private const byte DosExeHistoricFormat = 7;

    public static OfficialPackHeader Decode(ReadOnlySpan<byte> data, int offset)
    {
        if (offset < 0 || offset > data.Length - 20)
            throw new OfficialUpxMetadataException("PackHeader oficial truncado");
        if (!data.Slice(offset, 4).SequenceEqual("UPX!"u8))
            throw new OfficialUpxMetadataException("Magic do PackHeader oficial ausente");

        ReadOnlySpan<byte> header = data[offset..];
        byte version = header[4];
        byte format = header[5];
        int size = GetSize(version, format);
        if (header.Length < size)
            throw new OfficialUpxMetadataException("PackHeader oficial truncado");
        if (version == 0xFF)
            throw new OfficialUpxMetadataException("PackHeader oficial marcado como não descompactável");
        if (!Enum.IsDefined(typeof(CompressionMethod), header[6]))
            throw new OfficialUpxMetadataException($"Método UPX oficial desconhecido: {header[6]}");
        if (version >= 10 && header[size - 1] != ComputeChecksum(header[..(size - 1)]))
            throw new OfficialUpxMetadataException("Checksum do PackHeader oficial inválido");

        bool bigEndian = format >= 128;
        uint uAdler;
        uint cAdler;
        uint uLen;
        uint cLen;
        uint originalSize;
        int filterOffset;
        byte filterCto = 0;
        byte nMru = 0;

        if (bigEndian)
        {
            uLen = BinaryPrimitives.ReadUInt32BigEndian(header[8..12]);
            cLen = BinaryPrimitives.ReadUInt32BigEndian(header[12..16]);
            uAdler = BinaryPrimitives.ReadUInt32BigEndian(header[16..20]);
            cAdler = BinaryPrimitives.ReadUInt32BigEndian(header[20..24]);
            originalSize = BinaryPrimitives.ReadUInt32BigEndian(header[24..28]);
            filterOffset = 28;
            filterCto = header[29];
            nMru = header[30] == 0 ? (byte)0 : checked((byte)(header[30] + 1));
        }
        else
        {
            uAdler = BinaryPrimitives.ReadUInt32LittleEndian(header[8..12]);
            cAdler = BinaryPrimitives.ReadUInt32LittleEndian(header[12..16]);
            if (format is DosComFormat or DosSysFormat)
            {
                uLen = BinaryPrimitives.ReadUInt16LittleEndian(header[16..18]);
                cLen = BinaryPrimitives.ReadUInt16LittleEndian(header[18..20]);
                originalSize = uLen;
                filterOffset = 20;
            }
            else if (format is DosExeFormat or DosExeHistoricFormat)
            {
                uLen = ReadUInt24LittleEndian(header[16..19]);
                cLen = ReadUInt24LittleEndian(header[19..22]);
                originalSize = ReadUInt24LittleEndian(header[22..25]);
                filterOffset = 25;
            }
            else
            {
                uLen = BinaryPrimitives.ReadUInt32LittleEndian(header[16..20]);
                cLen = BinaryPrimitives.ReadUInt32LittleEndian(header[20..24]);
                originalSize = BinaryPrimitives.ReadUInt32LittleEndian(header[24..28]);
                filterOffset = 28;
                filterCto = header[29];
                nMru = header[30] == 0 ? (byte)0 : checked((byte)(header[30] + 1));
            }
        }

        byte level = (byte)(header[7] & 0x0F);
        byte filter = version >= 10
            ? header[filterOffset]
            : (header[7] & 0x80) == 0
                ? (byte)0
                : format is DosComFormat or DosSysFormat ? (byte)0x06 : (byte)0x26;

        if (uLen < 2 || cLen < 2 || uLen > UpxConstants.RsizeMax || cLen > UpxConstants.RsizeMax)
            throw new OfficialUpxMetadataException("Tamanhos do PackHeader oficial inválidos");

        return new OfficialPackHeader(
            offset,
            size,
            version,
            format,
            (CompressionMethod)header[6],
            level,
            uAdler,
            cAdler,
            uLen,
            cLen,
            originalSize,
            filter,
            filterCto,
            nMru);
    }

    public static bool TryFind(
        ReadOnlySpan<byte> data,
        int startOffset,
        int length,
        byte? expectedFormat,
        out OfficialPackHeader? header)
    {
        header = null;
        if (startOffset < 0 || length < 0 || startOffset > data.Length - length)
            return false;

        int end = startOffset + length;
        for (int offset = startOffset; offset <= end - 20; offset++)
        {
            if (!data.Slice(offset, 4).SequenceEqual("UPX!"u8))
                continue;

            try
            {
                OfficialPackHeader candidate = Decode(data, offset);
                if (expectedFormat is null || candidate.Format == expectedFormat.Value)
                {
                    header = candidate;
                    return true;
                }
            }
            catch (OfficialUpxMetadataException)
            {
                // Loader strings may contain UPX!; only a structurally valid header counts.
            }
        }

        return false;
    }

    public static int GetSize(byte version, byte format)
    {
        if (version <= 3)
            return 24;
        if (version <= 9)
            return format is DosComFormat or DosSysFormat
                ? 20
                : format is DosExeFormat or DosExeHistoricFormat ? 25 : 28;
        return format is DosComFormat or DosSysFormat
            ? 22
            : format is DosExeFormat or DosExeHistoricFormat ? 27 : 32;
    }

    private static byte ComputeChecksum(ReadOnlySpan<byte> headerWithoutChecksum)
    {
        int checksum = 0;
        foreach (byte value in headerWithoutChecksum[4..])
            checksum += value;
        return (byte)(checksum % 251);
    }

    private static uint ReadUInt24LittleEndian(ReadOnlySpan<byte> data)
        => (uint)(data[0] | data[1] << 8 | data[2] << 16);
}
