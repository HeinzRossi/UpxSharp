using System;
using System.Buffers.Binary;
using System.Linq;
using UpxSharp.Core;
using UpxSharp.Filters;
using Xunit;

namespace UpxSharp.Tests;

public class FilterTests
{
    [Fact]
    public void FilterX86_FilterAndUnfilter_RestoresOriginal()
    {
        byte[] code = [0x90, 0xE8, 0x04, 0x00, 0x00, 0x00, 0xE9, 0x02, 0x00, 0x00, 0x00, 0xC3];

        VerifyRoundTrip(new FilterX86(0x01), code, expectedCalls: 2);
    }

    [Fact]
    public void FilterArm32_FilterAndUnfilter_RestoresOriginal()
    {
        byte[] code = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(code, 0xEA000003);
        BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(4), 0xEB000004);

        VerifyRoundTrip(new FilterArm32(0x40), code, expectedCalls: 2);
    }

    [Fact]
    public void FilterArmThumb_FilterAndUnfilter_RestoresOriginal()
    {
        byte[] code = new byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(code, 0xE003);
        BinaryPrimitives.WriteUInt16LittleEndian(code.AsSpan(2), 0xE004);

        VerifyRoundTrip(new FilterArmThumb(0x50), code, expectedCalls: 2);
    }

    [Fact]
    public void FilterMips_FilterAndUnfilter_RestoresOriginal()
    {
        byte[] code = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(code, 0x08000003);
        BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(4), 0x0C000004);

        VerifyRoundTrip(new FilterMips(0x80), code, expectedCalls: 2);
    }

    [Fact]
    public void FilterPpc_FilterAndUnfilter_RestoresOriginal()
    {
        byte[] code = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(code, 0x4800000C);
        BinaryPrimitives.WriteUInt32BigEndian(code.AsSpan(4), 0x48000011);

        VerifyRoundTrip(new FilterPpc(0x90), code, expectedCalls: 2);
    }

    [Theory]
    [InlineData(0x01, typeof(FilterX86))]
    [InlineData(0x1F, typeof(FilterX86))]
    [InlineData(0x40, typeof(FilterArm32))]
    [InlineData(0x4F, typeof(FilterArm32))]
    [InlineData(0x50, typeof(FilterArmThumb))]
    [InlineData(0x5F, typeof(FilterArmThumb))]
    [InlineData(0x80, typeof(FilterMips))]
    [InlineData(0x8F, typeof(FilterMips))]
    [InlineData(0x90, typeof(FilterPpc))]
    [InlineData(0x9F, typeof(FilterPpc))]
    public void FilterFactory_Create_ReturnsExpectedFilter(byte id, Type expectedType)
        => Assert.IsType(expectedType, FilterFactory.Create(id));

    [Fact]
    public void FilterFactory_Create_WhenNone_ReturnsNull()
        => Assert.Null(FilterFactory.Create(0x00));

    [Fact]
    public void FilterFactory_Create_WhenUnknown_ThrowsCantPackException()
        => Assert.Throws<CantPackException>(() => FilterFactory.Create(0xFF));

    [Fact]
    public void FilterFactory_GetFiltersForArch_ReturnsX86Range()
    {
        byte[] filters = FilterFactory.GetFiltersForArch(UpxFormat.Win32Pe).ToArray();

        Assert.Equal(32, filters.Length);
        Assert.Equal(0x00, filters[0]);
        Assert.Equal(0x01, filters[1]);
        Assert.Equal(0x1F, filters[^1]);
    }

    [Fact]
    public void FilterFactory_GetFiltersForArch_ReturnsArmRanges()
    {
        byte[] filters = FilterFactory.GetFiltersForArch(UpxFormat.LinuxElfArm64LE).ToArray();

        Assert.Equal(31, filters.Length);
        Assert.Equal(0x00, filters[0]);
        Assert.Contains((byte)0x40, filters);
        Assert.Contains((byte)0x4F, filters);
        Assert.Contains((byte)0x50, filters);
        Assert.Contains((byte)0x5F, filters);
        Assert.DoesNotContain((byte)0x52, filters);
        Assert.DoesNotContain((byte)0x55, filters);
    }

    [Theory]
    [InlineData(UpxFormat.LinuxElfMipsel)]
    [InlineData(UpxFormat.LinuxElfMips)]
    public void FilterFactory_GetFiltersForArch_ReturnsMipsRange(UpxFormat format)
    {
        byte[] filters = FilterFactory.GetFiltersForArch(format).ToArray();

        Assert.Equal(17, filters.Length);
        Assert.Equal(0x00, filters[0]);
        Assert.Equal(0x80, filters[1]);
        Assert.Equal(0x8F, filters[^1]);
    }

    [Fact]
    public void FilterFactory_GetFiltersForArch_ReturnsPpcRange()
    {
        byte[] filters = FilterFactory.GetFiltersForArch(UpxFormat.LinuxElf32Ppc).ToArray();

        Assert.Equal(17, filters.Length);
        Assert.Equal(0x00, filters[0]);
        Assert.Equal(0x90, filters[1]);
        Assert.Equal(0x9F, filters[^1]);
    }

    [Fact]
    public void Filter_WhenNoBranch_DoesNotChangeBuffer()
    {
        byte[] code = Enumerable.Repeat((byte)0x90, 16).ToArray();
        byte[] original = code.ToArray();
        var state = new FilterState { Buffer = code, BufLen = code.Length };

        new FilterX86(0x01).Filter(state);

        Assert.Equal(original, code);
        Assert.Equal(0u, state.Calls);
    }

    private static void VerifyRoundTrip(IFilter filter, byte[] code, uint expectedCalls)
    {
        byte[] original = code.ToArray();
        var state = new FilterState
        {
            Buffer = code,
            BufLen = code.Length,
            AddValue = 0x1000,
        };

        filter.Filter(state);

        Assert.Equal(expectedCalls, state.Calls);
        Assert.NotEqual(original, code);

        filter.Unfilter(state);

        Assert.Equal(original, code);
    }
}
