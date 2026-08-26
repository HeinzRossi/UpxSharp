using UpxSharp.Compression;
using UpxSharp.Core;
using UpxSharp.Filters;
using UpxSharp.IO;
using UpxSharp.Linker;

namespace UpxSharp.Packers;

public abstract class Packer : PackerBase
{
    protected readonly Options Options;
    protected ElfLinker? Linker;
    protected PackHeader Header = new();

    protected Packer(InputFile file, Options options) : base(file)
        => Options = options;

    protected CompressResult CompressWithFilters(
        ReadOnlySpan<byte> code,
        IEnumerable<byte> filterIds,
        IEnumerable<int> levels)
    {
        var best = new CompressResult { CompressedSize = int.MaxValue };
        var combinations = filterIds
            .SelectMany(f => levels.Select(l => (FilterId: f, Level: l)))
            .ToList();

        int codeLength = code.Length;
        byte[] original = code.ToArray();
        object lockObj = new();

        Parallel.ForEach(combinations, combo =>
        {
            using var codeCopy = MemBuffer.FromSpan(original);
            var filterState = new FilterState
            {
                Id = combo.FilterId,
                Buffer = codeCopy.Data,
                BufLen = codeCopy.Length,
            };

            var filter = FilterFactory.Create(combo.FilterId);
            filter?.Filter(filterState);

            using var compressed = MemBuffer.Alloc(GetCompressionBufferSize(codeLength));
            var compressor = CompressorFactory.Create(Options.Method);
            int cLen = compressor.Compress(codeCopy.ReadOnlySpan, compressed.Span, combo.Level);
            if (cLen < 0) return;

            lock (lockObj)
            {
                if (cLen < best.CompressedSize)
                {
                    best = new CompressResult
                    {
                        FilterId = combo.FilterId,
                        FilterCto = filterState.Cto,
                        NMru = filterState.NMru,
                        FilterCalls = filterState.Calls,
                        Level = combo.Level,
                        CompressedSize = cLen,
                        CompressedData = compressed.Span[..cLen].ToArray(),
                        UAdler = Adler32.Compute(original),
                        CAdler = Adler32.Compute(compressed.ReadOnlySpan[..cLen]),
                    };
                }
            }
        });

        if (best.CompressedSize == int.MaxValue)
            throw new CantPackException("Dados não foram compressíveis com nenhuma combinação.");

        return best;
    }

    protected static bool CheckOverlap(
        int srcOffset, int srcSize, int dstOffset, int overwriteSize)
        => dstOffset + overwriteSize <= srcOffset || srcOffset + srcSize <= dstOffset;

    private static int GetCompressionBufferSize(int sourceLength)
        => checked(sourceLength + Math.Max(1024, sourceLength / 8 + 64));
}

public record CompressResult
{
    public byte FilterId { get; init; }
    public byte FilterCto { get; init; }
    public byte NMru { get; init; }
    public uint FilterCalls { get; init; }
    public int Level { get; init; }
    public int CompressedSize { get; init; }
    public byte[] CompressedData { get; init; } = [];
    public uint UAdler { get; init; }
    public uint CAdler { get; init; }
}
