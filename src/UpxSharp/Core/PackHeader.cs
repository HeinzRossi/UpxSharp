using System.Buffers.Binary;

namespace UpxSharp.Core;

public sealed class PackHeader
{
    public byte[] Magic { get; set; } = "UPX!"u8.ToArray();
    public byte Version { get; set; }
    public UpxFormat Format { get; set; }
    public CompressionMethod Method { get; set; }
    public byte Level { get; set; }
    public uint UAdler { get; set; }
    public uint CAdler { get; set; }
    public uint ULen { get; set; }
    public uint CLen { get; set; }
    public uint UFileSize { get; set; }
    public byte FilterId { get; set; }
    public byte FilterCto { get; set; }
    public byte NMru { get; set; }
    public PackPayloadKind PayloadKind { get; set; }

    public const int SerializedSize = 32;

    public void Write(Span<byte> buf)
    {
        if (buf.Length < SerializedSize)
            throw new ArgumentException("Buffer muito pequeno para PackHeader");

        Magic.CopyTo(buf);
        buf[4] = Version;
        buf[5] = (byte)Format;
        buf[6] = (byte)Method;
        buf[7] = Level;

        BinaryPrimitives.WriteUInt32LittleEndian(buf[8..], UAdler);
        BinaryPrimitives.WriteUInt32LittleEndian(buf[12..], CAdler);
        BinaryPrimitives.WriteUInt32LittleEndian(buf[16..], ULen);
        BinaryPrimitives.WriteUInt32LittleEndian(buf[20..], CLen);
        BinaryPrimitives.WriteUInt32LittleEndian(buf[24..], UFileSize);

        buf[28] = FilterId;
        buf[29] = FilterCto;
        buf[30] = NMru;
        buf[31] = (byte)PayloadKind;
    }

    public static PackHeader Read(ReadOnlySpan<byte> buf)
    {
        if (buf.Length < SerializedSize)
            throw new CorruptedPackException("PackHeader truncado");

        if (!buf[..4].SequenceEqual("UPX!"u8))
            throw new NotPackedException("Magic bytes UPX! não encontrados");

        return new PackHeader
        {
            Magic = buf[..4].ToArray(),
            Version = buf[4],
            Format = (UpxFormat)buf[5],
            Method = (CompressionMethod)buf[6],
            Level = buf[7],
            UAdler = BinaryPrimitives.ReadUInt32LittleEndian(buf[8..]),
            CAdler = BinaryPrimitives.ReadUInt32LittleEndian(buf[12..]),
            ULen = BinaryPrimitives.ReadUInt32LittleEndian(buf[16..]),
            CLen = BinaryPrimitives.ReadUInt32LittleEndian(buf[20..]),
            UFileSize = BinaryPrimitives.ReadUInt32LittleEndian(buf[24..]),
            FilterId = buf[28],
            FilterCto = buf[29],
            NMru = buf[30],
            PayloadKind = (PackPayloadKind)buf[31],
        };
    }

    public static bool TryFind(ReadOnlySpan<byte> data, out int offset, out PackHeader? header)
    {
        offset = -1;
        header = null;

        for (int i = data.Length - SerializedSize; i >= 0; i--)
        {
            if (data[i] == (byte)'U' && data[i + 1] == (byte)'P' &&
                data[i + 2] == (byte)'X' && data[i + 3] == (byte)'!')
            {
                try
                {
                    header = Read(data[i..]);
                    offset = i;
                    return true;
                }
                catch
                {
                }
            }
        }

        return false;
    }

    public static bool TryFindValidTrailer(ReadOnlySpan<byte> data, out int offset, out PackHeader? header)
    {
        offset = -1;
        header = null;

        int searchOffset = 0;
        while (searchOffset <= data.Length - SerializedSize)
        {
            if (!TryFind(data[searchOffset..], out int relativeOffset, out var candidate))
                return false;

            int absoluteOffset = searchOffset + relativeOffset;
            if (candidate is not null)
            {
                long endOffset = (long)absoluteOffset + SerializedSize + candidate.CLen;
                if (endOffset == data.Length && candidate.UFileSize == absoluteOffset)
                {
                    offset = absoluteOffset;
                    header = candidate;
                    return true;
                }
            }

            searchOffset = absoluteOffset + 1;
        }

        return false;
    }
}

public enum PackPayloadKind : byte
{
    SectionPatch = 0,
    FullImage = 1,
}
