namespace UpxSharp.Core;

public static class Adler32
{
    private const uint ModAdler = 65521;

    public static uint Compute(ReadOnlySpan<byte> data, uint initial = 1)
    {
        uint a = initial & 0xFFFF;
        uint b = (initial >> 16) & 0xFFFF;
        const int BlockSize = 5550;
        int offset = 0;

        while (offset < data.Length)
        {
            int blockEnd = Math.Min(offset + BlockSize, data.Length);
            for (int i = offset; i < blockEnd; i++)
            {
                a += data[i];
                b += a;
            }
            a %= ModAdler;
            b %= ModAdler;
            offset = blockEnd;
        }

        return (b << 16) | a;
    }

    public static void Verify(ReadOnlySpan<byte> data, uint expected, string context)
    {
        uint actual = Compute(data);
        if (actual != expected)
            throw new CorruptedPackException(
                $"Falha no checksum Adler-32 em {context}: esperado=0x{expected:X8}, calculado=0x{actual:X8}");
    }
}
