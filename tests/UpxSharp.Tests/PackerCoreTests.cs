using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UpxSharp.Compression;
using UpxSharp.Core;
using UpxSharp.IO;
using UpxSharp.Packers;
using UpxSharp.Packers.Windows;
using Xunit;

namespace UpxSharp.Tests;

public class PackerCoreTests
{
    [Fact]
    public void CompressWithFilters_DoesNotMutateInputAndFillsMetadata()
    {
        byte[] code = [0x90, 0xE8, 0x04, 0x00, 0x00, 0x00, 0xC3];
        byte[] original = code.ToArray();

        using var input = OpenTempInput([0]);
        var packer = new TestPacker(input.File, new Options { Method = CompressionMethod.Nrv2bLE32, Level = 1 });

        CompressResult result = packer.CompressForTest(code, [0x01], [1]);

        Assert.Equal(original, code);
        Assert.Equal(0x01, result.FilterId);
        Assert.Equal(1u, result.FilterCalls);
        Assert.Equal(Adler32.Compute(original), result.UAdler);
        Assert.Equal(Adler32.Compute(result.CompressedData), result.CAdler);
        Assert.True(result.CompressedSize > 0);
    }

    [Fact]
    public void CompressWithFilters_WhenCalledInParallel_DoesNotContaminateInput()
    {
        byte[] code = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        byte[] original = code.ToArray();

        using var input = OpenTempInput([0]);
        var packer = new TestPacker(input.File, new Options { Method = CompressionMethod.Nrv2dLE32, Level = 1 });

        Parallel.For(0, 20, _ =>
        {
            CompressResult result = packer.CompressForTest(code, [0x00, 0x01], [1, 3]);
            Assert.True(result.CompressedSize > 0);
        });

        Assert.Equal(original, code);
    }

    [Theory]
    [InlineData(0, 10, 10, 5, true)]
    [InlineData(10, 5, 0, 10, true)]
    [InlineData(0, 10, 9, 5, false)]
    [InlineData(5, 10, 0, 10, false)]
    public void CheckOverlap_ReturnsExpected(int srcOffset, int srcSize, int dstOffset, int overwriteSize, bool expected)
        => Assert.Equal(expected, TestPacker.CheckOverlapForTest(srcOffset, srcSize, dstOffset, overwriteSize));

    [Fact]
    public void PackMaster_FindPacker_WhenImplementedPe32_ReturnsPeFile32()
    {
        using var input = OpenTempInput(CreateMinimalPe(0x014C));

        var packer = PackMaster.FindPacker(input.File, new Options());

        Assert.IsType<PeFile32>(packer);
    }

    [Fact]
    public void PackMaster_FindPacker_WhenImplementedPe64_ReturnsPeFile64()
    {
        using var input = OpenTempInput(CreateMinimalPe(0x8664));

        var packer = PackMaster.FindPacker(input.File, new Options());

        Assert.IsType<PeFile64>(packer);
    }

    [Fact]
    public void PackMaster_FindPacker_WhenInvalid_ThrowsCantPackException()
    {
        using var input = OpenTempInput(Enumerable.Repeat((byte)0xCC, 512).ToArray());

        Assert.Throws<CantPackException>(() => PackMaster.FindPacker(input.File, new Options()));
    }

    [Fact]
    public void PackMaster_FindUnpacker_WhenImplementedPeFormatNotPacked_ThrowsNotPackedException()
    {
        using var input = OpenTempInput(CreatePackablePe(0x014C));

        Assert.Throws<NotPackedException>(() => PackMaster.FindUnpacker(input.File, new Options()));
    }

    [Theory]
    [InlineData(0x014C, typeof(PeFile32))]
    [InlineData(0x8664, typeof(PeFile64))]
    public void PackMaster_FindUnpacker_WhenImplementedPeFormatPacked_ReturnsExpectedPacker(ushort machine, Type expectedType)
    {
        byte[] packedBytes = Pack(CreatePackablePe(machine));
        using var input = OpenTempInput(packedBytes);

        var unpacker = PackMaster.FindUnpacker(input.File, new Options { Method = CompressionMethod.Nrv2bLE32, Level = 1 });

        Assert.IsType(expectedType, unpacker);
    }

    private static TempInput OpenTempInput(byte[] data)
    {
        string path = Path.Combine(Path.GetTempPath(), $"upxsharp-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, data);
        return new TempInput(path);
    }

    private static byte[] CreateMinimalPe(ushort machine)
    {
        byte[] data = new byte[512];
        data[0] = (byte)'M';
        data[1] = (byte)'Z';
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(60), 0x80);
        data[0x80] = (byte)'P';
        data[0x81] = (byte)'E';
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x84), machine);
        return data;
    }

    private static byte[] Pack(byte[] original)
    {
        using var input = OpenTempInput(original);
        string path = Path.Combine(Path.GetTempPath(), $"upxsharp-packed-{Guid.NewGuid():N}.bin");
        try
        {
            var options = new Options { Method = CompressionMethod.Lzma, Level = 9 };
            var packer = PackMaster.FindPacker(input.File, options);
            using (var output = new OutputFile(path, overwrite: true))
                packer.DoPack(output, options);
            return File.ReadAllBytes(path);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static byte[] CreatePackablePe(ushort machine)
    {
        bool is64 = machine == 0x8664;
        byte[] data = new byte[0x10000];
        data[0] = (byte)'M';
        data[1] = (byte)'Z';
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(60), 0x80);

        int peOffset = 0x80;
        data[peOffset] = (byte)'P';
        data[peOffset + 1] = (byte)'E';
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(peOffset + 4), machine);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(peOffset + 6), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(peOffset + 20), is64 ? (ushort)0xF0 : (ushort)0xE0);

        int optional = peOffset + 24;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(optional), is64 ? (ushort)0x20B : (ushort)0x10B);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(optional + 16), 0x1000);
        if (is64)
            BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(optional + 24), 0x140000000);
        else
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(optional + 28), 0x400000);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(optional + 60), 0x200);

        int section = optional + (is64 ? 0xF0 : 0xE0);
        Encoding.ASCII.GetBytes(".text\0\0\0").CopyTo(data.AsSpan(section, 8));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(section + 8), 0x40);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(section + 12), 0x1000);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(section + 16), 0x40);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(section + 20), 0x200);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(section + 36), 0x60000020);

        byte[] text =
        [
            0x90, 0x90, 0xE8, 0x04, 0x00, 0x00, 0x00, 0x90,
            0x90, 0xE9, 0x02, 0x00, 0x00, 0x00, 0x90, 0xC3
        ];
        text.CopyTo(data.AsSpan(0x200));
        return data;
    }

    private sealed class TempInput : IDisposable
    {
        private readonly string _path;

        public TempInput(string path)
        {
            _path = path;
            File = new InputFile(path);
        }

        public InputFile File { get; }

        public void Dispose()
        {
            File.Dispose();
            System.IO.File.Delete(_path);
        }
    }

    private sealed class TestPacker : Packer
    {
        public TestPacker(InputFile file, Options options) : base(file, options) { }

        public CompressResult CompressForTest(byte[] code, byte[] filters, int[] levels)
            => CompressWithFilters(code, filters, levels);

        public static bool CheckOverlapForTest(int srcOffset, int srcSize, int dstOffset, int overwriteSize)
            => CheckOverlap(srcOffset, srcSize, dstOffset, overwriteSize);

        public override UpxFormat GetFormat() => UpxFormat.Win32Pe;
        public override string GetName() => "test";
        public override byte GetVersion() => 0;
        public override bool IsBigEndian => false;
        public override bool? CanPack() => false;
        public override bool? CanUnpack() => false;
        public override void DoPack(OutputFile output, Options options) => throw new NotSupportedException();
        public override void DoUnpack(OutputFile output, Options options) => throw new NotSupportedException();
    }
}
