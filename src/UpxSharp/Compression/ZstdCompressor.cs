using UpxSharp.Core;
using ZstdSharp;

namespace UpxSharp.Compression;

public sealed class ZstdCompressor : ICompressor
{
    public CompressionMethod Method => CompressionMethod.Zstd;

    public int Compress(ReadOnlySpan<byte> source, Span<byte> destination, int level = 8)
    {
        using var compressor = new Compressor(Math.Clamp(level, 1, 22));
        byte[] input = source.ToArray();
        byte[] output = new byte[destination.Length];

        if (!compressor.TryWrap(
                new ArraySegment<byte>(input),
                new ArraySegment<byte>(output),
                out int written))
            return -1;

        output.AsSpan(0, written).CopyTo(destination);
        return written;
    }

    public void Decompress(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        using var decompressor = new Decompressor();
        byte[] input = source.ToArray();
        byte[] output = new byte[destination.Length];

        if (!decompressor.TryUnwrap(
                new ArraySegment<byte>(input),
                new ArraySegment<byte>(output),
                out int written))
            throw new CorruptedPackException("Falha ao descomprimir payload Zstd");

        if (written != destination.Length)
            throw new CorruptedPackException(
                $"Tamanho descomprimido Zstd inválido: esperado={destination.Length}, obtido={written}");

        output.CopyTo(destination);
    }
}
