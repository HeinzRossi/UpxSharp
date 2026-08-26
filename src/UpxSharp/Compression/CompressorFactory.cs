using UpxSharp.Core;

namespace UpxSharp.Compression;

public static class CompressorFactory
{
    public static ICompressor Create(CompressionMethod method) => method switch
    {
        CompressionMethod.Nrv2bLE32 or
        CompressionMethod.Nrv2b8 or
        CompressionMethod.Nrv2dLE32 or
        CompressionMethod.Nrv2d8 or
        CompressionMethod.Nrv2eLE32 => new UclCompressor(method),
        CompressionMethod.Nrv2e8 => new UclCompressor(method),
        CompressionMethod.Lzma => new LzmaCompressor(),
        CompressionMethod.Zstd => new ZstdCompressor(),
        _ => throw new CantPackException($"Método de compressão não suportado: {method}")
    };
}
