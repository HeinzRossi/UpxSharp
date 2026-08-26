using UpxSharp.Core;
using Xunit;

namespace UpxSharp.Tests;

public class Adler32Tests
{
    [Fact]
    public void Compute_WhenEmpty_ReturnsOne()
        => Assert.Equal(1u, Adler32.Compute([]));

    [Fact]
    public void Compute_KnownValue_ReturnsExpected()
    {
        var data = "Wikipedia"u8.ToArray();

        Assert.Equal(0x11E60398u, Adler32.Compute(data));
    }

    [Fact]
    public void Compute_WithInitialValue_ContinuesChecksum()
    {
        uint first = Adler32.Compute("Wiki"u8);
        uint combined = Adler32.Compute("pedia"u8, first);

        Assert.Equal(Adler32.Compute("Wikipedia"u8), combined);
    }

    [Fact]
    public void Verify_WhenChecksumMismatch_ThrowsCorruptedPackException()
    {
        var data = "bad data"u8.ToArray();

        var exception = Assert.Throws<CorruptedPackException>(() =>
            Adler32.Verify(data, 0u, "test"));

        Assert.Contains("Falha no checksum Adler-32", exception.Message);
    }
}
