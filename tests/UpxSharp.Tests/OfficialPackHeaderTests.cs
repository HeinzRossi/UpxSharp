using System;
using System.Buffers.Binary;
using UpxSharp.Core;
using UpxSharp.Official;
using UpxSharp.Filters;
using Xunit;

namespace UpxSharp.Tests;

public class OfficialPackHeaderTests
{
    [Fact]
    public void Decode_ModernPeHeader_ReadsFieldsAndChecksum()
    {
        byte[] data = new byte[96];
        Span<byte> header = data.AsSpan(32, 32);
        "UPX!"u8.CopyTo(header);
        header[4] = 14;
        header[5] = 9;
        header[6] = (byte)CompressionMethod.Nrv2bLE32;
        header[7] = 7;
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..12], 0x11223344);
        BinaryPrimitives.WriteUInt32LittleEndian(header[12..16], 0x55667788);
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..20], 4096);
        BinaryPrimitives.WriteUInt32LittleEndian(header[20..24], 1024);
        BinaryPrimitives.WriteUInt32LittleEndian(header[24..28], 8192);
        header[28] = 0x11;
        header[29] = 5;
        header[30] = 2;
        header[31] = ComputeChecksum(header[..31]);

        OfficialPackHeader result = OfficialPackHeaderDecoder.Decode(data, 32);

        Assert.Equal(32, result.Offset);
        Assert.Equal(32, result.Size);
        Assert.Equal((uint)4096, result.UncompressedLength);
        Assert.Equal((uint)1024, result.CompressedLength);
        Assert.Equal((uint)8192, result.OriginalFileSize);
        Assert.Equal((byte)3, result.NMru);
    }

    [Fact]
    public void Decode_ModernDosExeHeader_Reads24BitLengths()
    {
        byte[] data = new byte[27];
        "UPX!"u8.CopyTo(data);
        data[4] = 13;
        data[5] = 3;
        data[6] = (byte)CompressionMethod.Nrv2d8;
        data[7] = 5;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12, 4), 2);
        WriteUInt24(data.AsSpan(16, 3), 0x010203);
        WriteUInt24(data.AsSpan(19, 3), 0x000405);
        WriteUInt24(data.AsSpan(22, 3), 0x060708);
        data[25] = 0x06;
        data[26] = ComputeChecksum(data.AsSpan(0, 26));

        OfficialPackHeader result = OfficialPackHeaderDecoder.Decode(data, 0);

        Assert.Equal(27, result.Size);
        Assert.Equal((uint)0x010203, result.UncompressedLength);
        Assert.Equal((uint)0x000405, result.CompressedLength);
        Assert.Equal((uint)0x060708, result.OriginalFileSize);
    }

    [Fact]
    public void Decode_InvalidChecksum_ThrowsMetadataError()
    {
        byte[] data = new byte[32];
        "UPX!"u8.CopyTo(data);
        data[4] = 13;
        data[5] = 9;
        data[6] = (byte)CompressionMethod.Nrv2bLE32;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16, 4), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20, 4), 4);

        Assert.Throws<OfficialUpxMetadataException>(
            () => OfficialPackHeaderDecoder.Decode(data, 0));
    }

    [Theory]
    [InlineData(0x24, 0xE8)]
    [InlineData(0x46, 0xE9)]
    [InlineData(0x49, 0xE8)]
    public void OfficialX86CtoFilter_Unfilter_RestoresRelativeOperand(byte filterId, byte opcode)
    {
        byte[] data = new byte[16];
        data[2] = opcode;
        const byte cto = 0x13;
        const uint addValue = 0x1000;
        const uint relative = 0x20;
        uint encoded = relative + 2 + 1 + addValue + ((uint)cto << 24);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(3, 4), encoded);

        var state = new FilterState
        {
            Id = filterId,
            Buffer = data,
            BufLen = data.Length,
            Cto = cto,
            AddValue = addValue,
        };

        FilterFactory.Create(filterId)!.Unfilter(state);

        Assert.Equal(relative, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(3, 4)));
    }

    [Fact]
    public void OfficialArm64Filter_Unfilter_RestoresBranchImmediate()
    {
        byte[] data = new byte[16];
        uint original = 0x940001F0;
        uint encoded = (original & 0xFC000000) | (((original & 0x03FFFFFF) + 2) & 0x03FFFFFF);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8, 4), encoded);
        var state = new FilterState { Id = 0x52, Buffer = data, BufLen = data.Length };

        FilterFactory.Create(0x52)!.Unfilter(state);

        Assert.Equal(original, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8, 4)));
    }

    [Fact]
    public void OfficialRiscVFilter_Unfilter_RestoresAuipcPair()
    {
        byte[] data = new byte[0x270];
        Convert.FromHexString("17000002B0200100").CopyTo(data, 0x260);
        var state = new FilterState { Id = 0x55, Buffer = data, BufLen = data.Length };

        FilterFactory.Create(0x55)!.Unfilter(state);

        Assert.Equal("9700000012000000", Convert.ToHexString(data.AsSpan(0x260, 8)));
    }

    private static void WriteUInt24(Span<byte> destination, uint value)
    {
        destination[0] = (byte)value;
        destination[1] = (byte)(value >> 8);
        destination[2] = (byte)(value >> 16);
    }

    private static byte ComputeChecksum(ReadOnlySpan<byte> headerWithoutChecksum)
    {
        int checksum = 0;
        foreach (byte value in headerWithoutChecksum[4..])
            checksum += value;
        return (byte)(checksum % 251);
    }
}
