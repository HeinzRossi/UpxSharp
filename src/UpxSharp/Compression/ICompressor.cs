using UpxSharp.Core;

namespace UpxSharp.Compression;

public interface ICompressor
{
    CompressionMethod Method { get; }
    int Compress(ReadOnlySpan<byte> source, Span<byte> destination, int level = 8);
    void Decompress(ReadOnlySpan<byte> source, Span<byte> destination);
}
