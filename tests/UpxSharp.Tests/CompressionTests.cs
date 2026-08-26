using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using UpxSharp.Compression;
using UpxSharp.Core;
using Xunit;

namespace UpxSharp.Tests;

public class CompressionTests
{
    public static IEnumerable<object[]> RoundTripCases()
    {
        byte[] random = new byte[10_000];
        var rng = new Random(12345);
        rng.NextBytes(random);

        yield return [CompressionMethod.Lzma, Array.Empty<byte>()];
        yield return [CompressionMethod.Lzma, "Hello UPXSharp"u8.ToArray()];
        yield return [CompressionMethod.Lzma, Enumerable.Repeat((byte)0x42, 10_000).ToArray()];
        yield return [CompressionMethod.Lzma, random];

        yield return [CompressionMethod.Zstd, Array.Empty<byte>()];
        yield return [CompressionMethod.Zstd, "Hello UPXSharp"u8.ToArray()];
        yield return [CompressionMethod.Zstd, Enumerable.Repeat((byte)0x42, 10_000).ToArray()];
        yield return [CompressionMethod.Zstd, random];

        yield return [CompressionMethod.Nrv2bLE32, Array.Empty<byte>()];
        yield return [CompressionMethod.Nrv2bLE32, "Hello UPXSharp"u8.ToArray()];
        yield return [CompressionMethod.Nrv2bLE32, Enumerable.Repeat((byte)0x42, 10_000).ToArray()];
        yield return [CompressionMethod.Nrv2bLE32, random];

        yield return [CompressionMethod.Nrv2dLE32, Array.Empty<byte>()];
        yield return [CompressionMethod.Nrv2dLE32, "Hello UPXSharp"u8.ToArray()];
        yield return [CompressionMethod.Nrv2dLE32, Enumerable.Repeat((byte)0x42, 10_000).ToArray()];
        yield return [CompressionMethod.Nrv2dLE32, random];

        yield return [CompressionMethod.Nrv2eLE32, Array.Empty<byte>()];
        yield return [CompressionMethod.Nrv2eLE32, "Hello UPXSharp"u8.ToArray()];
        yield return [CompressionMethod.Nrv2eLE32, Enumerable.Repeat((byte)0x42, 10_000).ToArray()];
        yield return [CompressionMethod.Nrv2eLE32, random];
    }

    [Theory]
    [MemberData(nameof(RoundTripCases))]
    public void CompressAndDecompress_RoundTrips(CompressionMethod method, byte[] original)
    {
        var compressor = CompressorFactory.Create(method);
        byte[] compressed = new byte[GetCompressionBufferSize(original.Length)];

        int compressedLength = compressor.Compress(original, compressed, level: 6);

        Assert.True(compressedLength > 0);
        if (method is CompressionMethod.Nrv2bLE32 or CompressionMethod.Nrv2dLE32 or CompressionMethod.Nrv2eLE32)
            Assert.NotEqual(0x2156524Eu, BinaryPrimitives.ReadUInt32LittleEndian(compressed));

        byte[] restored = new byte[original.Length];
        compressor.Decompress(compressed.AsSpan(0, compressedLength), restored);

        Assert.Equal(original, restored);
    }

    [Theory]
    [InlineData(CompressionMethod.Lzma)]
    [InlineData(CompressionMethod.Zstd)]
    [InlineData(CompressionMethod.Nrv2bLE32)]
    [InlineData(CompressionMethod.Nrv2dLE32)]
    [InlineData(CompressionMethod.Nrv2eLE32)]
    public void Compress_WhenDestinationTooSmall_ReturnsMinusOne(CompressionMethod method)
    {
        var compressor = CompressorFactory.Create(method);
        byte[] source = Enumerable.Repeat((byte)0x7A, 1024).ToArray();
        byte[] destination = new byte[1];

        int compressedLength = compressor.Compress(source, destination, level: 6);

        Assert.Equal(-1, compressedLength);
    }

    [Fact]
    public void CompressorFactory_CreateLzma_ReturnsLzmaCompressor()
        => Assert.IsType<LzmaCompressor>(CompressorFactory.Create(CompressionMethod.Lzma));

    [Fact]
    public void CompressorFactory_CreateZstd_ReturnsZstdCompressor()
        => Assert.IsType<ZstdCompressor>(CompressorFactory.Create(CompressionMethod.Zstd));

    [Theory]
    [InlineData(CompressionMethod.Nrv2bLE32)]
    [InlineData(CompressionMethod.Nrv2b8)]
    [InlineData(CompressionMethod.Nrv2dLE32)]
    [InlineData(CompressionMethod.Nrv2d8)]
    [InlineData(CompressionMethod.Nrv2eLE32)]
    [InlineData(CompressionMethod.Nrv2e8)]
    public void CompressorFactory_CreateNrvSupportedDecodeMethods_ReturnsUclCompressor(CompressionMethod method)
        => Assert.IsType<UclCompressor>(CompressorFactory.Create(method));

    [Theory]
    [InlineData(CompressionMethod.Nrv2bLE16)]
    [InlineData(CompressionMethod.Nrv2dLE16)]
    [InlineData(CompressionMethod.Nrv2eLE16)]
    public void CompressorFactory_WhenNrvVariantUnsupported_ThrowsCantPackException(CompressionMethod method)
        => Assert.Throws<CantPackException>(() => CompressorFactory.Create(method));

    [Fact]
    public void CompressorFactory_WhenMethodUnsupported_ThrowsCantPackException()
        => Assert.Throws<CantPackException>(() => CompressorFactory.Create(CompressionMethod.Deflate));

    [Fact]
    public void UclCompressor_Decompress_WhenPayloadTruncated_ThrowsCorruptedPackException()
    {
        var compressor = new UclCompressor(CompressionMethod.Nrv2bLE32);
        byte[] compressed = CreateRawScaffold(CompressionMethod.Nrv2bLE32, "UPX"u8.ToArray());

        Assert.Throws<CorruptedPackException>(() =>
            compressor.Decompress(compressed.AsSpan(0, compressed.Length - 1), new byte[3]));
    }

    [Fact]
    public void UclCompressor_Decompress_WhenOutputTooSmall_ThrowsCorruptedPackException()
    {
        var compressor = new UclCompressor(CompressionMethod.Nrv2dLE32);
        byte[] compressed = CreateRawScaffold(CompressionMethod.Nrv2dLE32, "UPX"u8.ToArray());

        Assert.Throws<CorruptedPackException>(() =>
            compressor.Decompress(compressed, new byte[2]));
    }

    [Fact]
    public void UclCompressor_Decompress_WhenTrailingDataExists_ThrowsCorruptedPackException()
    {
        var compressor = new UclCompressor(CompressionMethod.Nrv2eLE32);
        byte[] compressed = CreateRawScaffold(CompressionMethod.Nrv2eLE32, "UPX"u8.ToArray());
        byte[] withTrailingData = [.. compressed, 0x00, 0x00, 0x00, 0x00];

        Assert.Throws<CorruptedPackException>(() =>
            compressor.Decompress(withTrailingData, new byte[3]));
    }

    [Fact]
    public void UclCompressor_Decompress_WhenMatchOffsetInvalid_ThrowsCorruptedPackException()
    {
        var compressor = new UclCompressor(CompressionMethod.Nrv2bLE32);
        byte[] compressed = CreateInvalidOffsetPayload(CompressionMethod.Nrv2bLE32);

        Assert.Throws<CorruptedPackException>(() =>
            compressor.Decompress(compressed, new byte[1]));
    }

    [Theory]
    [InlineData(CompressionMethod.Nrv2bLE32)]
    [InlineData(CompressionMethod.Nrv2dLE32)]
    [InlineData(CompressionMethod.Nrv2eLE32)]
    public void UclCompressor_Compress_EmitsRealNrvWithoutScaffoldMagic(CompressionMethod method)
    {
        var compressor = new UclCompressor(method);
        byte[] compressed = new byte[GetCompressionBufferSize(3)];

        int compressedLength = compressor.Compress("UPX"u8, compressed);

        Assert.True(compressedLength > 0);
        Assert.NotEqual(0x2156524Eu, BinaryPrimitives.ReadUInt32LittleEndian(compressed));
    }

    [Theory]
    [InlineData(CompressionMethod.Nrv2b8)]
    [InlineData(CompressionMethod.Nrv2d8)]
    [InlineData(CompressionMethod.Nrv2e8)]
    public void UclCompressor_Compress_WhenNrv8_ThrowsCantPackException(CompressionMethod method)
    {
        var compressor = new UclCompressor(method);

        Assert.Throws<CantPackException>(() => compressor.Compress("UPX"u8, new byte[128]));
    }

    [Theory]
    [InlineData(CompressionMethod.Nrv2bLE32, "")]
    [InlineData(CompressionMethod.Nrv2bLE32, "UPX")]
    [InlineData(CompressionMethod.Nrv2b8, "")]
    [InlineData(CompressionMethod.Nrv2b8, "UPX")]
    [InlineData(CompressionMethod.Nrv2dLE32, "")]
    [InlineData(CompressionMethod.Nrv2dLE32, "UPX")]
    [InlineData(CompressionMethod.Nrv2d8, "")]
    [InlineData(CompressionMethod.Nrv2d8, "UPX")]
    [InlineData(CompressionMethod.Nrv2eLE32, "")]
    [InlineData(CompressionMethod.Nrv2eLE32, "UPX")]
    [InlineData(CompressionMethod.Nrv2e8, "")]
    [InlineData(CompressionMethod.Nrv2e8, "UPX")]
    public void UclCompressor_DecompressRealNrvLiteralFixture_RestoresBytes(CompressionMethod method, string text)
    {
        byte[] expected = System.Text.Encoding.ASCII.GetBytes(text);
        byte[] compressed = CreateRealNrvLiteralFixture(method, expected);
        var compressor = new UclCompressor(method);
        byte[] restored = new byte[expected.Length];

        compressor.Decompress(compressed, restored);

        Assert.Equal(expected, restored);
    }

    [Fact]
    public void UclCompressor_DecompressRealNrv_WhenTrailingDataExists_ThrowsCorruptedPackException()
    {
        var compressor = new UclCompressor(CompressionMethod.Nrv2bLE32);
        byte[] compressed = CreateRealNrvLiteralFixture(CompressionMethod.Nrv2bLE32, "UPX"u8.ToArray());
        byte[] withTrailingData = [.. compressed, 0x00];

        Assert.Throws<CorruptedPackException>(() =>
            compressor.Decompress(withTrailingData, new byte[3]));
    }

    [Fact]
    public void UclCompressor_DecompressRealNrv_WhenOutputTooSmall_ThrowsCorruptedPackException()
    {
        var compressor = new UclCompressor(CompressionMethod.Nrv2dLE32);
        byte[] compressed = CreateRealNrvLiteralFixture(CompressionMethod.Nrv2dLE32, "UPX"u8.ToArray());

        Assert.Throws<CorruptedPackException>(() =>
            compressor.Decompress(compressed, new byte[2]));
    }

    private static byte[] Compress(ICompressor compressor, byte[] original)
    {
        byte[] compressed = new byte[GetCompressionBufferSize(original.Length)];
        int compressedLength = compressor.Compress(original, compressed, level: 6);
        Assert.True(compressedLength > 0);
        return compressed[..compressedLength];
    }

    private static byte[] CreateRawScaffold(CompressionMethod method, byte[] original)
    {
        byte[] compressed = new byte[16 + original.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(compressed, 0x2156524E);
        compressed[4] = (byte)method;
        compressed[5] = 1;
        compressed[6] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(compressed.AsSpan(8), (uint)original.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(compressed.AsSpan(12), Adler32.Compute(original));
        original.CopyTo(compressed.AsSpan(16));
        return compressed;
    }

    private static byte[] CreateInvalidOffsetPayload(CompressionMethod method)
    {
        byte[] compressed = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(compressed, 0x2156524E);
        compressed[4] = (byte)method;
        compressed[5] = 1;
        compressed[6] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(compressed.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(compressed.AsSpan(12), Adler32.Compute([0]));
        BinaryPrimitives.WriteUInt32LittleEndian(compressed.AsSpan(16), 0x00000006);
        BinaryPrimitives.WriteUInt32LittleEndian(compressed.AsSpan(20), 0x00000004);
        return compressed;
    }

    private static byte[] CreateRealNrvLiteralFixture(CompressionMethod method, byte[] literals)
    {
        if (method is CompressionMethod.Nrv2b8 or CompressionMethod.Nrv2d8 or CompressionMethod.Nrv2e8)
            return CreateRealNrv8LiteralFixture(method, literals);

        var compressor = new UclCompressor(method);
        byte[] compressed = new byte[GetCompressionBufferSize(literals.Length)];
        int compressedLength = compressor.Compress(literals, compressed);
        Assert.True(compressedLength > 0);
        return compressed[..compressedLength];
    }

    private static byte[] CreateRealNrv8LiteralFixture(CompressionMethod method, byte[] literals)
    {
        var writer = new ByteBitWriter(new byte[GetCompressionBufferSize(literals.Length)]);
        foreach (byte literal in literals)
        {
            Assert.True(writer.TryWriteBit(true));
            Assert.True(writer.TryWriteByte(literal));
        }

        Assert.True(writer.TryWriteBit(false));
        foreach (bool bit in CreateEofBits(method))
            Assert.True(writer.TryWriteBit(bit));
        Assert.True(writer.TryWriteByte(0xFF));

        return writer.ToArray();
    }

    private static IEnumerable<bool> CreateEofBits(CompressionMethod method)
    {
        const uint eofPrefix = 0x1000002;
        return method == CompressionMethod.Nrv2b8
            ? Create2BOffsetBits(eofPrefix)
            : Create2DOr2EOffsetBits(eofPrefix);
    }

    private static IEnumerable<bool> Create2BOffsetBits(uint target)
    {
        var bits = new List<bool>();
        Build2BPrefix(target / 2, bits);
        bits.Add((target & 1) != 0);
        bits.Add(true);
        return bits;
    }

    private static void Build2BPrefix(uint state, List<bool> bits)
    {
        if (state == 1)
            return;

        Build2BPrefix(state / 2, bits);
        bits.Add((state & 1) != 0);
        bits.Add(false);
    }

    private static IEnumerable<bool> Create2DOr2EOffsetBits(uint target)
    {
        for (uint b1 = 0; b1 <= 1; b1++)
        {
            if ((target - b1) % 2 != 0)
                continue;

            uint prefixState = (target - b1) / 2;
            var bits = new List<bool>();
            if (Build2DOr2EPrefix(prefixState, bits))
            {
                bits.Add(b1 != 0);
                bits.Add(true);
                return bits;
            }
        }

        throw new InvalidOperationException("Could not generate NRV EOF");
    }

    private static bool Build2DOr2EPrefix(uint state, List<bool> bits)
    {
        if (state == 1)
            return true;

        for (uint b1 = 0; b1 <= 1; b1++)
            for (uint b2 = 0; b2 <= 1; b2++)
            {
                uint numerator = state + 2 - 2 * b1 - b2;
                if (numerator % 4 != 0)
                    continue;

                uint previous = numerator / 4;
                if (previous < 1 || previous >= state)
                    continue;

                if (!Build2DOr2EPrefix(previous, bits))
                    continue;

                bits.Add(b1 != 0);
                bits.Add(false);
                bits.Add(b2 != 0);
                return true;
            }

        return false;
    }

    private static int GetCompressionBufferSize(int length)
        => length + Math.Max(1024, length / 8 + 64);

    private ref struct ByteBitWriter
    {
        private Span<byte> _destination;
        private int _offset;
        private int _byteOffset;
        private int _bitCount;

        public ByteBitWriter(Span<byte> destination)
        {
            _destination = destination;
            _offset = 0;
            _byteOffset = -1;
            _bitCount = 0;
        }

        public bool TryWriteBit(bool bit)
        {
            if (_bitCount == 0)
            {
                if (_offset >= _destination.Length)
                    return false;

                _byteOffset = _offset;
                _destination[_offset++] = 0;
            }

            if (bit)
                _destination[_byteOffset] |= (byte)(1 << (7 - _bitCount));

            _bitCount = (_bitCount + 1) & 7;
            return true;
        }

        public bool TryWriteByte(byte value)
        {
            if (_offset >= _destination.Length)
                return false;

            _destination[_offset++] = value;
            return true;
        }

        public byte[] ToArray() => _destination[.._offset].ToArray();
    }
}
