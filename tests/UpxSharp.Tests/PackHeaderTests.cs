using System;
using UpxSharp.Core;
using Xunit;

namespace UpxSharp.Tests;

public class PackHeaderTests
{
    [Fact]
    public void WriteAndRead_RoundtripPreservesFields()
    {
        var header = new PackHeader
        {
            Version = 1,
            Format = UpxFormat.LinuxElfi386,
            Method = CompressionMethod.Lzma,
            Level = 7,
            UAdler = 0x12345678,
            CAdler = 0x87654321,
            ULen = 0x1000,
            CLen = 0x0800,
            UFileSize = 0x2000,
            FilterId = 0x01,
            FilterCto = 0x02,
            NMru = 0x03,
        };

        var buffer = new byte[PackHeader.SerializedSize];
        header.Write(buffer);
        var read = PackHeader.Read(buffer);

        Assert.Equal(header.Version, read.Version);
        Assert.Equal(header.Format, read.Format);
        Assert.Equal(header.Method, read.Method);
        Assert.Equal(header.Level, read.Level);
        Assert.Equal(header.UAdler, read.UAdler);
        Assert.Equal(header.CAdler, read.CAdler);
        Assert.Equal(header.ULen, read.ULen);
        Assert.Equal(header.CLen, read.CLen);
        Assert.Equal(header.UFileSize, read.UFileSize);
        Assert.Equal(header.FilterId, read.FilterId);
        Assert.Equal(header.FilterCto, read.FilterCto);
        Assert.Equal(header.NMru, read.NMru);
    }

    [Fact]
    public void Read_WhenBufferTooShort_ThrowsCorruptedPackException()
        => Assert.Throws<CorruptedPackException>(() => PackHeader.Read(new byte[PackHeader.SerializedSize - 1]));

    [Fact]
    public void Read_WhenMagicInvalid_ThrowsNotPackedException()
    {
        var buffer = new byte[PackHeader.SerializedSize];

        Assert.Throws<NotPackedException>(() => PackHeader.Read(buffer));
    }

    [Fact]
    public void TryFind_WhenHeaderExists_ReturnsOffsetAndHeader()
    {
        var header = new PackHeader
        {
            Version = 2,
            Format = UpxFormat.Win32Pe,
            Method = CompressionMethod.Zstd,
            Level = 5,
        };
        var buffer = new byte[128];
        header.Write(buffer.AsSpan(48));

        bool found = PackHeader.TryFind(buffer, out int offset, out var read);

        Assert.True(found);
        Assert.Equal(48, offset);
        Assert.NotNull(read);
        Assert.Equal(header.Format, read!.Format);
        Assert.Equal(header.Method, read.Method);
    }

    [Fact]
    public void TryFind_WhenHeaderMissing_ReturnsFalse()
    {
        bool found = PackHeader.TryFind(new byte[128], out int offset, out var header);

        Assert.False(found);
        Assert.Equal(-1, offset);
        Assert.Null(header);
    }

    [Fact]
    public void TryFindValidTrailer_WhenInternalMagicExistsWithoutTrailer_ReturnsFalse()
    {
        var header = new PackHeader
        {
            Version = 2,
            Format = UpxFormat.Win32Pe,
            Method = CompressionMethod.Nrv2bLE32,
            Level = 1,
            CLen = 4,
        };
        var buffer = new byte[128];
        header.Write(buffer.AsSpan(16));

        Assert.True(PackHeader.TryFind(buffer, out int internalOffset, out _));
        Assert.Equal(16, internalOffset);

        bool found = PackHeader.TryFindValidTrailer(buffer, out int offset, out var trailer);

        Assert.False(found);
        Assert.Equal(-1, offset);
        Assert.Null(trailer);
    }

    [Fact]
    public void TryFindValidTrailer_WhenHeaderAndCompressedPayloadEndAtFileEnd_ReturnsTrue()
    {
        var header = new PackHeader
        {
            Version = 2,
            Format = UpxFormat.Win32Pe,
            Method = CompressionMethod.Nrv2bLE32,
            Level = 1,
            CLen = 8,
        };
        var buffer = new byte[80];
        int headerOffset = buffer.Length - PackHeader.SerializedSize - (int)header.CLen;
        header.UFileSize = (uint)headerOffset;
        header.Write(buffer.AsSpan(headerOffset));

        bool found = PackHeader.TryFindValidTrailer(buffer, out int offset, out var trailer);

        Assert.True(found);
        Assert.Equal(headerOffset, offset);
        Assert.NotNull(trailer);
        Assert.Equal(header.CLen, trailer!.CLen);
    }
}
