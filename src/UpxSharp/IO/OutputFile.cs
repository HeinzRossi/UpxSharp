namespace UpxSharp.IO;

public sealed class OutputFile : IDisposable
{
    private readonly FileStream _stream;
    private long _position;
    private bool _disposed;

    public string Name => _stream.Name;

    public OutputFile(string path, bool overwrite = false)
    {
        _stream = new FileStream(path,
            overwrite ? FileMode.Create : FileMode.CreateNew,
            FileAccess.Write, FileShare.None,
            bufferSize: 65536);
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        ThrowIfDisposed();
        _stream.Write(data);
        _position += data.Length;
    }

    public void Rewrite(ReadOnlySpan<byte> data, long offset)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        long saved = _stream.Position;
        _stream.Seek(offset, SeekOrigin.Begin);
        _stream.Write(data);
        _stream.Seek(saved, SeekOrigin.Begin);
    }

    public long Position => _position;

    public void Flush()
    {
        ThrowIfDisposed();
        _stream.Flush();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stream.Dispose();
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, nameof(OutputFile));
}
