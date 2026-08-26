using System;
using System.Linq;
using UpxSharp.Core;
using Xunit;

namespace UpxSharp.Tests;

public class MemoryTests
{
    [Fact]
    public void MemBuffer_Alloc_ReturnsBufferOfRequestedSize()
    {
        using var buffer = MemBuffer.Alloc(1024);
        Assert.Equal(1024, buffer.Length);
        Assert.Equal(1024, buffer.Span.Length);
    }

    [Fact]
    public void MemBuffer_Alloc_WhenSizeIsZero_ThrowsArgumentOutOfRangeException()
        => Assert.Throws<ArgumentOutOfRangeException>(() => MemBuffer.Alloc(0));

    [Fact]
    public void MemBuffer_Alloc_WhenSizeExceedsMax_ThrowsUpxException()
        => Assert.Throws<UpxException>(() => MemBuffer.Alloc((int)MemBuffer.MaxSize + 1));

    [Fact]
    public void MemBuffer_FromSpan_CopiesData()
    {
        var source = "UPX"u8.ToArray();
        using var buffer = MemBuffer.FromSpan(source);

        Assert.Equal(source.Length, buffer.Length);
        Assert.Equal(source, buffer.Data.Take(buffer.Length).ToArray());
    }

    [Fact]
    public void MemBuffer_Slice_ReturnsExpectedWindow()
    {
        using var buffer = MemBuffer.FromSpan([1, 2, 3, 4, 5]);

        Assert.Equal([2, 3, 4], buffer.Slice(1, 3).ToArray());
    }

    [Fact]
    public void MemBuffer_WhenDisposed_ThrowsObjectDisposedExceptionOnAccess()
    {
        var buffer = MemBuffer.Alloc(8);
        buffer.Dispose();

        Assert.Throws<ObjectDisposedException>(() => buffer.Data);
    }
}
