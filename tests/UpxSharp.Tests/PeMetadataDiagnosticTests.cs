using System;
using System.Buffers.Binary;
using System.IO;
using UpxSharp.Core;
using UpxSharp.Official;
using Xunit;

namespace UpxSharp.Tests;

public class PeMetadataTests
{
    [Fact]
    public void OfficialPeMetadata_ContainsCompactImportTable()
    {
        byte[] packed = File.ReadAllBytes(@"C:\upxsharp-artifacts\upx-packed-fixtures\Win32Pe-Win32Pe-handle.exe.upx");
        OfficialUpxMetadata metadata = OfficialUpxUnpacker.ReadMetadata(packed, UpxFormat.Win32Pe);
        byte[] payload = OfficialUpxExtentReader.DecompressBlock(packed, metadata.Blocks![0] with { FilterId = 0 });
        int extra = (int)BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(payload.Length - 4));
        ushort sections = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(extra + 6));
        ushort optional = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(extra + 20));
        int cursor = extra + 24 + optional + sections * 40;
        uint compact = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(cursor));
        uint names = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(cursor + 4));
        Assert.InRange(extra, 1, payload.Length - 1);
        Assert.InRange(cursor, 1, payload.Length - 9);
        Assert.InRange(compact, 1u, (uint)payload.Length - 1);
        Assert.Equal(0u, names);
        Assert.NotEqual(0u, BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan((int)compact)));
    }
}
