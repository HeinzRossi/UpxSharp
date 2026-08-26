namespace UpxSharp.IO;

public sealed class InputFile : IDisposable
{
    private readonly FileStream _stream;

    public string Name { get; }
    public long Length => _stream.Length;

    public InputFile(string path)
    {
        Name = path;
        _stream = new FileStream(path,
            FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 65536, FileOptions.RandomAccess);
    }

    public void ReadExact(Span<byte> buffer, long offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = RandomAccess.Read(
                _stream.SafeFileHandle,
                buffer[totalRead..],
                offset + totalRead);

            if (read == 0)
                throw new EndOfStreamException(
                    $"ReadExact: esperava {buffer.Length} bytes, leu {totalRead} em '{Name}' offset {offset}");

            totalRead += read;
        }
    }

    public int Read(Span<byte> buffer, long offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        return RandomAccess.Read(_stream.SafeFileHandle, buffer, offset);
    }

    public void Dispose() => _stream.Dispose();
}
