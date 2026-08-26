using ManagedLzma;
using ManagedLzma.LZMA;
using System.Buffers.Binary;
using UpxSharp.Core;

namespace UpxSharp.Compression;

public sealed class LzmaCompressor : ICompressor
{
    public CompressionMethod Method => CompressionMethod.Lzma;

    public int Compress(ReadOnlySpan<byte> source, Span<byte> destination, int level = 8)
    {
        var settings = new EncoderSettings();
        settings.SetLevel(Math.Clamp(level, 0, 9));
        settings.DictionarySize = level switch
        {
            <= 3 => 1 << 16,
            <= 6 => 1 << 20,
            <= 8 => 1 << 23,
            _ => 1 << 25,
        };
        settings.ReduceSize = source.Length;

        using var encoder = new AsyncEncoder(settings);
        var reader = new ReadOnlyMemoryStreamReader(source.ToArray());
        var writer = new MemoryStreamWriter();

        encoder.EncodeAsync(reader, writer, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        byte[] compressed = writer.ToArray();
        byte[] props = settings.GetDecoderSettings().ToArray();
        int resultLength = props.Length + sizeof(long) + compressed.Length;

        if (resultLength > destination.Length)
            return -1;

        props.CopyTo(destination);
        BinaryPrimitives.WriteInt64LittleEndian(destination[props.Length..], source.Length);
        compressed.CopyTo(destination[(props.Length + sizeof(long))..]);
        return resultLength;
    }

    public void Decompress(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        const int propsLength = 5;
        const int headerLength = propsLength + sizeof(long);

        if (source.Length < headerLength)
            throw new CorruptedPackException("Payload LZMA truncado");

        long originalLength = BinaryPrimitives.ReadInt64LittleEndian(source[propsLength..headerLength]);
        if (originalLength != destination.Length)
            throw new CorruptedPackException(
                $"Tamanho LZMA inválido: esperado={destination.Length}, informado={originalLength}");

        byte[] sourceArray = source.ToArray();
        var settings = DecoderSettings.ReadFrom(sourceArray, 0);
        using var decoder = new Decoder(settings);

        byte[] output = new byte[destination.Length];
        int inputOffset = headerLength;
        int outputOffset = 0;

        while (outputOffset < output.Length && !decoder.IsOutputComplete)
        {
            int consumed = decoder.Decode(
                sourceArray,
                inputOffset,
                sourceArray.Length - inputOffset,
                output.Length - outputOffset,
                eof: true);

            inputOffset += consumed;

            while (decoder.AvailableOutputLength > 0 && outputOffset < output.Length)
            {
                outputOffset += decoder.ReadOutputData(
                    output,
                    outputOffset,
                    output.Length - outputOffset);
            }

            if (consumed == 0 && decoder.AvailableOutputLength == 0 && !decoder.IsOutputComplete)
                throw new CorruptedPackException("Stream LZMA incompleto");
        }

        if (outputOffset != output.Length)
            throw new CorruptedPackException(
                $"Tamanho descomprimido LZMA inválido: esperado={output.Length}, obtido={outputOffset}");

        output.CopyTo(destination);
    }

    private sealed class ReadOnlyMemoryStreamReader : IStreamReader
    {
        private readonly byte[] _data;
        private int _offset;

        public ReadOnlyMemoryStreamReader(byte[] data) => _data = data;

        public Task<int> ReadAsync(byte[] buffer, int offset, int length, StreamMode mode)
        {
            int count = Math.Min(length, _data.Length - _offset);
            if (count > 0)
            {
                Array.Copy(_data, _offset, buffer, offset, count);
                _offset += count;
            }

            return Task.FromResult(count);
        }
    }

    private sealed class MemoryStreamWriter : IStreamWriter
    {
        private readonly MemoryStream _stream = new();

        public Task<int> WriteAsync(byte[] buffer, int offset, int length, StreamMode mode)
        {
            _stream.Write(buffer, offset, length);
            return Task.FromResult(length);
        }

        public Task CompleteAsync() => Task.CompletedTask;

        public byte[] ToArray() => _stream.ToArray();
    }
}
