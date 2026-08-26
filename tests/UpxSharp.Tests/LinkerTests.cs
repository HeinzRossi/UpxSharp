using System;
using System.Buffers.Binary;
using System.Linq;
using UpxSharp.Core;
using UpxSharp.Linker;
using Xunit;

namespace UpxSharp.Tests;

public class LinkerTests
{
    public static TheoryData<Func<ElfLinker>, bool> Linkers()
        => new()
        {
            { () => new ElfLinkerAmd64(), false },
            { () => new ElfLinkerArm32(), false },
            { () => new ElfLinkerArm64(), false },
            { () => new ElfLinkerMips(), false },
            { () => new ElfLinkerPpc(), true },
        };

    [Theory]
    [MemberData(nameof(Linkers))]
    public void DefineSymbolAndGetSymbol_RoundTripsValue(Func<ElfLinker> createLinker, bool _)
    {
        var linker = createLinker();

        linker.DefineSymbol("_patch32", 0x12345678);

        Assert.Equal(0x12345678u, linker.GetSymbol("_patch32"));
    }

    [Theory]
    [MemberData(nameof(Linkers))]
    public void GetSymbol_WhenMissing_ThrowsInternalErrorException(Func<ElfLinker> createLinker, bool _)
    {
        var linker = createLinker();

        Assert.Throws<InternalErrorException>(() => linker.GetSymbol("missing"));
    }

    [Theory]
    [MemberData(nameof(Linkers))]
    public void Relocate_ReturnsCloneAndDoesNotMutateOriginalStub(Func<ElfLinker> createLinker, bool isBigEndian)
    {
        byte[] stub = CreateStub();
        byte[] original = stub.ToArray();
        var linker = createLinker();
        linker.Init(stub);
        linker.DefineSymbol("_patch32", 0xAABBCCDD);

        byte[] relocated = linker.Relocate();

        Assert.NotSame(stub, relocated);
        Assert.Equal(original, stub);
        uint patched = isBigEndian
            ? BinaryPrimitives.ReadUInt32BigEndian(relocated)
            : BinaryPrimitives.ReadUInt32LittleEndian(relocated);
        Assert.Equal(0xAABBCCDDu, patched);
        Assert.NotEqual(original, relocated);
    }

    [Theory]
    [MemberData(nameof(Linkers))]
    public void GetLoaderSection_ReturnsParsedFakeSection(Func<ElfLinker> createLinker, bool _)
    {
        var linker = createLinker();
        linker.Init(CreateStub());

        (int offset, int size) = linker.GetLoaderSection(".loader");

        Assert.Equal(12, offset);
        Assert.Equal(4, size);
    }

    [Theory]
    [MemberData(nameof(Linkers))]
    public void GetLoaderSection_WhenMissing_ThrowsInternalErrorException(Func<ElfLinker> createLinker, bool _)
    {
        var linker = createLinker();
        linker.Init(CreateStub());

        Assert.Throws<InternalErrorException>(() => linker.GetLoaderSection(".missing"));
    }

    private static byte[] CreateStub()
    {
        byte[] stub = new byte[16];
        stub[0] = (byte)'U';
        stub[1] = (byte)'P';
        stub[2] = (byte)'X';
        stub[3] = (byte)'L';
        BinaryPrimitives.WriteInt32LittleEndian(stub.AsSpan(4), 12);
        BinaryPrimitives.WriteInt32LittleEndian(stub.AsSpan(8), 4);
        stub[12] = 1;
        stub[13] = 2;
        stub[14] = 3;
        stub[15] = 4;
        return stub;
    }
}
