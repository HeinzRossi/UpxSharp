using UpxSharp.Compression;
using UpxSharp.Core;
using UpxSharp.Filters;

namespace UpxSharp.Official;

public sealed class OfficialUpxExtentReader
{
    private readonly ReadOnlyMemory<byte> _data;
    private readonly IReadOnlyList<OfficialUpxBlock> _blocks;
    private int _index;

    public OfficialUpxExtentReader(
        ReadOnlyMemory<byte> data,
        IReadOnlyList<OfficialUpxBlock> blocks)
    {
        _data = data;
        _blocks = blocks;
    }

    public int RemainingBlocks => _blocks.Count - _index;

    public void Reset() => _index = 0;

    public byte[] ReadExtent(ulong wanted)
    {
        if (wanted > int.MaxValue)
            throw new OfficialUpxMetadataException("Extent UPX oficial excede limite suportado");

        byte[] extent = new byte[(int)wanted];
        int written = 0;
        while (written < extent.Length)
        {
            if (_index >= _blocks.Count)
                throw new OfficialUpxMetadataException("Extent UPX oficial truncado");

            OfficialUpxBlock block = _blocks[_index++];
            if (block.UncompressedSize > extent.Length - written)
                throw new OfficialUpxMetadataException("Bloco UPX oficial ultrapassa limite do extent");

            byte[] decompressed = DecompressBlock(_data.Span, block);
            decompressed.CopyTo(extent, written);
            written += decompressed.Length;
        }

        return extent;
    }

    public void EnsureFullyConsumed()
    {
        if (_index != _blocks.Count)
            throw new OfficialUpxMetadataException("Dados UPX oficiais nao consumidos apos reconstruir extents");
    }

    internal static byte[] DecompressBlock(ReadOnlySpan<byte> data, OfficialUpxBlock block)
    {
        if (block.PayloadOffset < 0
            || block.CompressedSize > int.MaxValue
            || block.PayloadOffset > data.Length - (int)block.CompressedSize)
            throw new OfficialUpxMetadataException("Bloco UPX oficial aponta para fora do arquivo");

        ReadOnlySpan<byte> compressed =
            data.Slice(block.PayloadOffset, checked((int)block.CompressedSize));
        byte[] decompressed = new byte[block.UncompressedSize];

        try
        {
            if (block.CompressedSize == block.UncompressedSize)
            {
                compressed.CopyTo(decompressed);
            }
            else
            {
                var compressor = CompressorFactory.Create(block.Method);
                compressor.Decompress(compressed, decompressed);
            }
        }
        catch (Exception ex) when (ex is UpxException or ArgumentException or InvalidOperationException)
        {
            throw new OfficialUpxDecompressException(
                $"Falha ao descomprimir bloco UPX oficial em 0x{block.HeaderOffset:X}: {ex.Message}");
        }

        if (block.FilterId == 0)
            return decompressed;

        var filter = FilterFactory.Create(block.FilterId);
        if (filter is null)
            throw new OfficialUpxMetadataException(
                $"Filtro UPX oficial desconhecido: 0x{block.FilterId:X2}");

        try
        {
            var state = new FilterState
            {
                Id = block.FilterId,
                Buffer = decompressed,
                BufLen = decompressed.Length,
                Cto = block.FilterCto,
            };
            filter.Unfilter(state);
        }
        catch (Exception ex) when (ex is UpxException or ArgumentException or InvalidOperationException)
        {
            throw new OfficialUpxDecompressException(
                $"Falha ao aplicar filtro UPX oficial 0x{block.FilterId:X2}: {ex.Message}");
        }

        return decompressed;
    }
}
