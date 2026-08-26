using System.Buffers;

namespace UpxSharp.Core;

public sealed class MemBuffer : IDisposable
{
    public const long MaxSize = 768L * 1024 * 1024;

    private byte[]? _array;
    private bool _disposed;

    public byte[] Data => _array ?? throw new ObjectDisposedException(nameof(MemBuffer));
    public int Length { get; private set; }
    public Span<byte> Span => Data.AsSpan(0, Length);
    public ReadOnlySpan<byte> ReadOnlySpan => Data.AsSpan(0, Length);
    public Memory<byte> Memory => Data.AsMemory(0, Length);

    private MemBuffer() { }

    public static MemBuffer Alloc(int size, bool clearMemory = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        if (size > MaxSize)
            throw new UpxException(
                $"Tamanho de buffer excede UPX_RSIZE_MAX: {size} > {MaxSize}");

        var buf = new MemBuffer
        {
            _array = ArrayPool<byte>.Shared.Rent(size),
            Length = size
        };

        if (clearMemory)
            buf.Span.Clear();

        return buf;
    }

    public static MemBuffer FromArray(byte[] data)
        => new() { _array = data, Length = data.Length };

    public static MemBuffer FromSpan(ReadOnlySpan<byte> data)
    {
        var buf = Alloc(data.Length, clearMemory: false);
        data.CopyTo(buf.Span);
        return buf;
    }

    public Span<byte> Slice(int start, int length)
        => Data.AsSpan(start, length);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_array is not null)
        {
            ArrayPool<byte>.Shared.Return(_array);
            _array = null;
        }
    }
}
