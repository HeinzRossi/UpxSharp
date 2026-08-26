using System.Buffers.Binary;

namespace UpxSharp.Endian;

public static class BinaryEndian
{
    public static ushort GetLE16(ReadOnlySpan<byte> buf, int offset = 0)
        => BinaryPrimitives.ReadUInt16LittleEndian(buf[offset..]);

    public static uint GetLE32(ReadOnlySpan<byte> buf, int offset = 0)
        => BinaryPrimitives.ReadUInt32LittleEndian(buf[offset..]);

    public static ulong GetLE64(ReadOnlySpan<byte> buf, int offset = 0)
        => BinaryPrimitives.ReadUInt64LittleEndian(buf[offset..]);

    public static void SetLE16(Span<byte> buf, ushort value, int offset = 0)
        => BinaryPrimitives.WriteUInt16LittleEndian(buf[offset..], value);

    public static void SetLE32(Span<byte> buf, uint value, int offset = 0)
        => BinaryPrimitives.WriteUInt32LittleEndian(buf[offset..], value);

    public static void SetLE64(Span<byte> buf, ulong value, int offset = 0)
        => BinaryPrimitives.WriteUInt64LittleEndian(buf[offset..], value);

    public static ushort GetBE16(ReadOnlySpan<byte> buf, int offset = 0)
        => BinaryPrimitives.ReadUInt16BigEndian(buf[offset..]);

    public static uint GetBE32(ReadOnlySpan<byte> buf, int offset = 0)
        => BinaryPrimitives.ReadUInt32BigEndian(buf[offset..]);

    public static ulong GetBE64(ReadOnlySpan<byte> buf, int offset = 0)
        => BinaryPrimitives.ReadUInt64BigEndian(buf[offset..]);

    public static void SetBE32(Span<byte> buf, uint value, int offset = 0)
        => BinaryPrimitives.WriteUInt32BigEndian(buf[offset..], value);

    public static void SetBE16(Span<byte> buf, ushort value, int offset = 0)
        => BinaryPrimitives.WriteUInt16BigEndian(buf[offset..], value);

    public static void SetBE64(Span<byte> buf, ulong value, int offset = 0)
        => BinaryPrimitives.WriteUInt64BigEndian(buf[offset..], value);

    public static uint GetTE32(ReadOnlySpan<byte> buf, bool isBigEndian, int offset = 0)
        => isBigEndian ? GetBE32(buf, offset) : GetLE32(buf, offset);

    public static void SetTE32(Span<byte> buf, uint value, bool isBigEndian, int offset = 0)
    {
        if (isBigEndian) SetBE32(buf, value, offset);
        else SetLE32(buf, value, offset);
    }

    public static ushort GetTE16(ReadOnlySpan<byte> buf, bool isBigEndian, int offset = 0)
        => isBigEndian ? GetBE16(buf, offset) : GetLE16(buf, offset);

    public static void SetTE16(Span<byte> buf, ushort value, bool isBigEndian, int offset = 0)
    {
        if (isBigEndian) SetBE16(buf, value, offset);
        else SetLE16(buf, value, offset);
    }
}
