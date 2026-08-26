using System.Buffers.Binary;
using UpxSharp.Core;

namespace UpxSharp.Compression;

public sealed class UclCompressor : ICompressor
{
    private const uint Magic = 0x2156524E; // "NRV!"
    private const int HeaderSize = 16;

    public UclCompressor(CompressionMethod method)
    {
        if (!IsSupported(method))
            throw new CantPackException($"Metodo NRV nao suportado nesta etapa: {method}");

        Method = method;
    }

    public CompressionMethod Method { get; }

    public int Compress(ReadOnlySpan<byte> source, Span<byte> destination, int level = 8)
    {
        if (Method is CompressionMethod.Nrv2b8 or CompressionMethod.Nrv2d8 or CompressionMethod.Nrv2e8)
            throw new CantPackException($"Encoder NRV 8-bit pendente: {Method}");

        return NrvEncoder.EncodeLiteralOnly(Method, source, destination);
    }

    public void Decompress(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (source.Length >= HeaderSize && BinaryPrimitives.ReadUInt32LittleEndian(source) == Magic)
        {
            DecompressRawScaffold(source, destination);
            return;
        }

        NrvDecoder.Decode(Method, source, destination);
    }

    public static bool IsSupported(CompressionMethod method)
        => method is CompressionMethod.Nrv2bLE32
            or CompressionMethod.Nrv2b8
            or CompressionMethod.Nrv2dLE32
            or CompressionMethod.Nrv2d8
            or CompressionMethod.Nrv2eLE32
            or CompressionMethod.Nrv2e8;

    private void DecompressRawScaffold(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (source.Length < HeaderSize)
            throw new CorruptedPackException("Payload NRV truncado");

        var method = (CompressionMethod)source[4];
        if (method != Method)
            throw new CorruptedPackException($"Metodo NRV invalido: esperado={Method}, informado={method}");

        uint originalLength = BinaryPrimitives.ReadUInt32LittleEndian(source[8..]);
        if (originalLength != destination.Length)
            throw new CorruptedPackException(
                $"Tamanho NRV invalido: esperado={destination.Length}, informado={originalLength}");

        uint expectedAdler = BinaryPrimitives.ReadUInt32LittleEndian(source[12..]);

        if (source[6] != 0)
            throw new CorruptedPackException($"Modo NRV scaffold invalido: {source[6]}");

        if (source.Length != HeaderSize + destination.Length)
            throw new CorruptedPackException("Tamanho de payload NRV raw invalido");

        source[HeaderSize..].CopyTo(destination);
        Adler32.Verify(destination, expectedAdler, "NRV");
    }

    private static class NrvDecoder
    {
        public static void Decode(CompressionMethod method, ReadOnlySpan<byte> source, Span<byte> destination)
        {
            IBitReader reader = method switch
            {
                CompressionMethod.Nrv2b8 or CompressionMethod.Nrv2d8 or CompressionMethod.Nrv2e8 => new ByteBitReader(source),
                _ => new Le32BitReader(source),
            };
            int outputOffset = 0;
            uint lastMatchOffset = 1;

            while (true)
            {
                while (reader.ReadBit())
                {
                    if (reader.InputOffset >= source.Length)
                        throw new CorruptedPackException("Payload NRV truncado");
                    if (outputOffset >= destination.Length)
                        throw new CorruptedPackException("Payload NRV excede tamanho de saida");

                    destination[outputOffset++] = source[reader.InputOffset++];
                }

                uint matchOffset;
                uint matchLength;
                switch (method)
                {
                    case CompressionMethod.Nrv2bLE32:
                    case CompressionMethod.Nrv2b8:
                        (matchOffset, matchLength, lastMatchOffset) = Decode2B(reader, source, outputOffset, lastMatchOffset);
                        break;
                    case CompressionMethod.Nrv2dLE32:
                    case CompressionMethod.Nrv2d8:
                        (matchOffset, matchLength, lastMatchOffset) = Decode2D(reader, source, outputOffset, lastMatchOffset);
                        break;
                    case CompressionMethod.Nrv2eLE32:
                    case CompressionMethod.Nrv2e8:
                        (matchOffset, matchLength, lastMatchOffset) = Decode2E(reader, source, outputOffset, lastMatchOffset);
                        break;
                    default:
                        throw new CantPackException($"Metodo NRV nao suportado nesta etapa: {method}");
                }

                if (matchOffset == uint.MaxValue)
                    break;

                CopyMatch(destination, ref outputOffset, matchOffset, matchLength);
            }

            if (outputOffset != destination.Length)
                throw new CorruptedPackException(
                    $"Tamanho descomprimido NRV invalido: esperado={destination.Length}, obtido={outputOffset}");

            if (reader.InputOffset != source.Length)
                throw new CorruptedPackException("Dados extras apos payload NRV");
        }

        private static (uint Offset, uint Length, uint LastOffset) Decode2B(
            IBitReader reader,
            ReadOnlySpan<byte> source,
            int outputOffset,
            uint lastMatchOffset)
        {
            uint matchOffset = 1;
            do
            {
                matchOffset = checked(matchOffset * 2 + reader.ReadBitUInt32());
                if (reader.InputOffset >= source.Length)
                    throw new CorruptedPackException("Payload NRV truncado");
                if (matchOffset > 0xFFFFFF + 3u)
                    throw new CorruptedPackException("Referencia NRV invalida");
            }
            while (!reader.ReadBit());

            if (matchOffset == 2)
            {
                matchOffset = lastMatchOffset;
            }
            else
            {
                if (reader.InputOffset >= source.Length)
                    throw new CorruptedPackException("Payload NRV truncado");

                matchOffset = unchecked((matchOffset - 3) * 256 + source[reader.InputOffset++]);
                if (matchOffset == uint.MaxValue)
                    return (uint.MaxValue, 0, lastMatchOffset);

                lastMatchOffset = ++matchOffset;
            }

            uint matchLength = reader.ReadBitUInt32();
            matchLength = matchLength * 2 + reader.ReadBitUInt32();
            if (matchLength == 0)
                matchLength = DecodeLongLength(reader, source, outputOffset) + 2;

            matchLength += matchOffset > 0xD00 ? 1u : 0u;
            return (matchOffset, matchLength, lastMatchOffset);
        }

        private static (uint Offset, uint Length, uint LastOffset) Decode2D(
            IBitReader reader,
            ReadOnlySpan<byte> source,
            int outputOffset,
            uint lastMatchOffset)
        {
            uint matchOffset = Decode2DOr2EOffset(reader, source, ref lastMatchOffset, out uint matchLength);
            if (matchOffset == uint.MaxValue)
                return (uint.MaxValue, 0, lastMatchOffset);

            matchLength = matchLength * 2 + reader.ReadBitUInt32();
            if (matchLength == 0)
                matchLength = DecodeLongLength(reader, source, outputOffset) + 2;

            matchLength += matchOffset > 0x500 ? 1u : 0u;
            return (matchOffset, matchLength, lastMatchOffset);
        }

        private static (uint Offset, uint Length, uint LastOffset) Decode2E(
            IBitReader reader,
            ReadOnlySpan<byte> source,
            int outputOffset,
            uint lastMatchOffset)
        {
            uint matchOffset = Decode2DOr2EOffset(reader, source, ref lastMatchOffset, out uint matchLength);
            if (matchOffset == uint.MaxValue)
                return (uint.MaxValue, 0, lastMatchOffset);

            if (matchLength != 0)
                matchLength = 1 + reader.ReadBitUInt32();
            else if (reader.ReadBit())
                matchLength = 3 + reader.ReadBitUInt32();
            else
                matchLength = DecodeLongLength(reader, source, outputOffset) + 3;

            matchLength += matchOffset > 0x500 ? 1u : 0u;
            return (matchOffset, matchLength, lastMatchOffset);
        }

        private static uint Decode2DOr2EOffset(
            IBitReader reader,
            ReadOnlySpan<byte> source,
            ref uint lastMatchOffset,
            out uint matchLength)
        {
            uint matchOffset = 1;
            while (true)
            {
                matchOffset = checked(matchOffset * 2 + reader.ReadBitUInt32());
                if (reader.InputOffset >= source.Length)
                    throw new CorruptedPackException("Payload NRV truncado");
                if (matchOffset > 0xFFFFFF + 3u)
                    throw new CorruptedPackException("Referencia NRV invalida");

                if (reader.ReadBit())
                    break;

                matchOffset = checked((matchOffset - 1) * 2 + reader.ReadBitUInt32());
            }

            if (matchOffset == 2)
            {
                matchOffset = lastMatchOffset;
                matchLength = reader.ReadBitUInt32();
            }
            else
            {
                if (reader.InputOffset >= source.Length)
                    throw new CorruptedPackException("Payload NRV truncado");

                matchOffset = unchecked((matchOffset - 3) * 256 + source[reader.InputOffset++]);
                if (matchOffset == uint.MaxValue)
                {
                    matchLength = 0;
                    return uint.MaxValue;
                }

                matchLength = (matchOffset ^ uint.MaxValue) & 1;
                matchOffset >>= 1;
                lastMatchOffset = ++matchOffset;
            }

            return matchOffset;
        }

        private static uint DecodeLongLength(IBitReader reader, ReadOnlySpan<byte> source, int outputOffset)
        {
            uint matchLength = 1;
            do
            {
                matchLength = checked(matchLength * 2 + reader.ReadBitUInt32());
                if (reader.InputOffset >= source.Length)
                    throw new CorruptedPackException("Payload NRV truncado");
                if (matchLength > int.MaxValue)
                    throw new CorruptedPackException("Match NRV excede tamanho de saida");
            }
            while (!reader.ReadBit());

            return matchLength;
        }

        private static void CopyMatch(Span<byte> destination, ref int outputOffset, uint matchOffset, uint matchLength)
        {
            if (matchOffset == 0 || matchOffset > outputOffset)
                throw new CorruptedPackException("Referencia NRV invalida");

            ulong totalBytes = (ulong)matchLength + 1;
            if ((ulong)outputOffset + totalBytes > (ulong)destination.Length)
                throw new CorruptedPackException("Match NRV excede tamanho de saida");

            int sourceOffset = outputOffset - (int)matchOffset;
            destination[outputOffset++] = destination[sourceOffset++];
            do
            {
                destination[outputOffset++] = destination[sourceOffset++];
            }
            while (--matchLength > 0);
        }
    }

    private static class NrvEncoder
    {
        public static int EncodeLiteralOnly(CompressionMethod method, ReadOnlySpan<byte> source, Span<byte> destination)
        {
            var writer = new Le32BitWriter(destination);
            foreach (byte literal in source)
            {
                if (!writer.TryWriteBit(true) || !writer.TryWriteByte(literal))
                    return -1;
            }

            if (!writer.TryWriteBit(false))
                return -1;

            foreach (bool bit in CreateEofBits(method))
            {
                if (!writer.TryWriteBit(bit))
                    return -1;
            }

            if (!writer.TryWriteByte(0xFF))
                return -1;

            return writer.Length;
        }

        private static IEnumerable<bool> CreateEofBits(CompressionMethod method)
        {
            const uint eofPrefix = 0x1000002;
            return method == CompressionMethod.Nrv2bLE32
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

            throw new InternalErrorException("Nao foi possivel gerar EOF NRV");
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
    }

    private interface IBitReader
    {
        int InputOffset { get; set; }
        bool ReadBit();
        uint ReadBitUInt32();
    }

    private sealed class Le32BitReader : IBitReader
    {
        private readonly ReadOnlyMemory<byte> _source;
        private uint _bits;
        private int _count;

        public Le32BitReader(ReadOnlySpan<byte> source)
            => _source = source.ToArray();

        public int InputOffset { get; set; }

        public bool ReadBit()
        {
            if (_count == 0)
                FillWord();

            bool value = (_bits & 0x80000000u) != 0;
            _bits <<= 1;
            _count--;
            return value;
        }

        public uint ReadBitUInt32() => ReadBit() ? 1u : 0u;

        private void FillWord()
        {
            if (InputOffset + 4 > _source.Length)
                throw new CorruptedPackException("Bitstream NRV truncado");

            _bits = BinaryPrimitives.ReadUInt32LittleEndian(_source.Span[InputOffset..]);
            InputOffset += 4;
            _count = 32;
        }
    }

    private sealed class ByteBitReader : IBitReader
    {
        private readonly ReadOnlyMemory<byte> _source;
        private byte _bits;
        private int _count;

        public ByteBitReader(ReadOnlySpan<byte> source)
            => _source = source.ToArray();

        public int InputOffset { get; set; }

        public bool ReadBit()
        {
            if (_count == 0)
                FillByte();

            bool value = (_bits & 0x80) != 0;
            _bits <<= 1;
            _count--;
            return value;
        }

        public uint ReadBitUInt32() => ReadBit() ? 1u : 0u;

        private void FillByte()
        {
            if (InputOffset >= _source.Length)
                throw new CorruptedPackException("Bitstream NRV truncado");

            _bits = _source.Span[InputOffset++];
            _count = 8;
        }
    }

    private ref struct Le32BitWriter
    {
        private Span<byte> _destination;
        private int _offset;
        private int _wordOffset;
        private int _bitCount;

        public Le32BitWriter(Span<byte> destination)
        {
            _destination = destination;
            _offset = 0;
            _wordOffset = -1;
            _bitCount = 0;
        }

        public int Length => _offset;

        public bool TryWriteBit(bool bit)
        {
            if (_bitCount == 0)
            {
                if (_offset + 4 > _destination.Length)
                    return false;

                _wordOffset = _offset;
                _destination.Slice(_offset, 4).Clear();
                _offset += 4;
            }

            if (bit)
            {
                uint word = BinaryPrimitives.ReadUInt32LittleEndian(_destination[_wordOffset..]);
                word |= 1u << (31 - _bitCount);
                BinaryPrimitives.WriteUInt32LittleEndian(_destination[_wordOffset..], word);
            }

            _bitCount = (_bitCount + 1) & 31;
            return true;
        }

        public bool TryWriteByte(byte value)
        {
            if (_offset >= _destination.Length)
                return false;

            _destination[_offset++] = value;
            return true;
        }
    }
}
