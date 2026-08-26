using UpxSharp.Endian;
using Xunit;

namespace UpxSharp.Tests;

public class EndianTests
{
    [Fact]
    public void BinaryEndian_LE16LE32LE64_Roundtrip()
    {
        var buffer = new byte[16];

        BinaryEndian.SetLE16(buffer, 0x0102, offset: 1);
        BinaryEndian.SetLE32(buffer, 0x01020304, offset: 3);
        BinaryEndian.SetLE64(buffer, 0x0102030405060708, offset: 7);

        Assert.Equal(0x0102, BinaryEndian.GetLE16(buffer, offset: 1));
        Assert.Equal(0x01020304u, BinaryEndian.GetLE32(buffer, offset: 3));
        Assert.Equal(0x0102030405060708ul, BinaryEndian.GetLE64(buffer, offset: 7));
    }

    [Fact]
    public void BinaryEndian_BE16BE32BE64_Roundtrip()
    {
        var buffer = new byte[16];

        BinaryEndian.SetBE16(buffer, 0x0102, offset: 1);
        BinaryEndian.SetBE32(buffer, 0x01020304, offset: 3);
        BinaryEndian.SetBE64(buffer, 0x0102030405060708, offset: 7);

        Assert.Equal(0x0102, BinaryEndian.GetBE16(buffer, offset: 1));
        Assert.Equal(0x01020304u, BinaryEndian.GetBE32(buffer, offset: 3));
        Assert.Equal(0x0102030405060708ul, BinaryEndian.GetBE64(buffer, offset: 7));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BinaryEndian_TE16TE32_Roundtrip(bool bigEndian)
    {
        var buffer = new byte[8];

        BinaryEndian.SetTE16(buffer, 0x1122, bigEndian, offset: 1);
        BinaryEndian.SetTE32(buffer, 0x33445566, bigEndian, offset: 3);

        Assert.Equal(0x1122, BinaryEndian.GetTE16(buffer, bigEndian, offset: 1));
        Assert.Equal(0x33445566u, BinaryEndian.GetTE32(buffer, bigEndian, offset: 3));
    }
}
