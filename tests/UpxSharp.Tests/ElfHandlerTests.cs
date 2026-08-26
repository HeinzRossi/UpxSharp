using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using UpxSharp.Core;
using UpxSharp.IO;
using UpxSharp.Packers;
using UpxSharp.Packers.Linux;
using Xunit;

namespace UpxSharp.Tests;

public class ElfHandlerTests
{
    public static TheoryData<bool, ushort, bool, Type> ElfFormats => new()
    {
        { false, 3, false, typeof(PackLinuxElf32x86) },
        { true, 62, false, typeof(PackLinuxElf64Amd) },
        { false, 40, false, typeof(PackLinuxElf32Arm) },
        { true, 183, false, typeof(PackLinuxElf64Arm) },
        { false, 8, false, typeof(PackLinuxElf32Mips) },
        { false, 8, true, typeof(PackLinuxElf32MipsBe) },
        { true, 243, false, typeof(PackLinuxElf64Riscv) },
        { false, 20, true, typeof(PackLinuxElf32Ppc) },
        { true, 21, true, typeof(PackLinuxElf64Ppc) },
        { true, 21, false, typeof(PackLinuxElf64Ppc) },
    };

    [Theory]
    [MemberData(nameof(ElfFormats))]
    public void PackMaster_FindPacker_DetectsSyntheticElf(bool is64, ushort machine, bool isBigEndian, Type expectedType)
    {
        using var input = TempInput.Create(CreateSyntheticElf(is64, machine, isBigEndian));

        var packer = PackMaster.FindPacker(input.File, new Options { Method = CompressionMethod.Nrv2bLE32, Level = 1 });

        Assert.IsType(expectedType, packer);
    }

    [Theory]
    [MemberData(nameof(ElfFormats))]
    public void ElfPacker_PackAndUnpack_RestoresOriginalBytes(bool is64, ushort machine, bool isBigEndian, Type expectedType)
    {
        _ = expectedType;
        byte[] original = CreateSyntheticElf(is64, machine, isBigEndian);
        using var input = TempInput.Create(original);
        using var packed = TempOutput.Create();
        using var unpacked = TempOutput.Create();
        var options = new Options { Method = CompressionMethod.Nrv2bLE32, Level = 1 };

        var packer = PackMaster.FindPacker(input.File, options);
        packer.DoPack(packed.File, options);
        packed.DisposeFile();

        byte[] packedBytes = File.ReadAllBytes(packed.Path);
        Assert.True(packedBytes.Length > original.Length);
        Assert.Contains("UPX!", Encoding.ASCII.GetString(packedBytes));

        using var packedInput = TempInput.OpenExisting(packed.Path);
        var unpacker = PackMaster.FindUnpacker(packedInput.File, options);
        unpacker.DoUnpack(unpacked.File, options);
        unpacked.DisposeFile();

        Assert.Equal(original, File.ReadAllBytes(unpacked.Path));
    }

    [Theory]
    [MemberData(nameof(ElfFormats))]
    public void PackMaster_FindUnpacker_WhenElfPacked_ReturnsExpectedPacker(bool is64, ushort machine, bool isBigEndian, Type expectedType)
    {
        byte[] packedBytes = Pack(CreateSyntheticElf(is64, machine, isBigEndian));
        using var input = TempInput.Create(packedBytes);

        var unpacker = PackMaster.FindUnpacker(input.File, new Options { Method = CompressionMethod.Nrv2bLE32, Level = 1 });

        Assert.IsType(expectedType, unpacker);
    }

    [Fact]
    public void PackMaster_FindUnpacker_WhenElfNotPacked_ThrowsNotPackedException()
    {
        using var input = TempInput.Create(CreateSyntheticElf(is64: false, machine: 3));

        Assert.Throws<NotPackedException>(() => PackMaster.FindUnpacker(input.File, new Options()));
    }

    [Fact]
    public void PackMaster_FindPacker_WhenElfContainsInternalUpxMagic_DoesNotTreatAsPacked()
    {
        byte[] original = CreateSyntheticElf(is64: false, machine: 20, isBigEndian: true);
        "UPX!"u8.CopyTo(original.AsSpan(0x120));
        using var input = TempInput.Create(original);

        var packer = PackMaster.FindPacker(input.File, new Options { Method = CompressionMethod.Nrv2bLE32, Level = 1 });

        Assert.IsType<PackLinuxElf32Ppc>(packer);
    }

    [Fact]
    public void ElfPacker_Unpack_WhenPayloadTruncated_ThrowsCorruptedPackException()
    {
        byte[] packedBytes = Pack(CreateSyntheticElf(is64: false, machine: 3));
        byte[] truncated = packedBytes[..^1];
        using var input = TempInput.Create(truncated);
        using var output = TempOutput.Create();
        var options = new Options { Method = CompressionMethod.Nrv2bLE32, Level = 1 };
        var unpacker = PackMaster.FindUnpacker(input.File, options);

        Assert.Throws<CorruptedPackException>(() => unpacker.DoUnpack(output.File, options));
    }

    [Fact]
    public void ElfPacker_Unpack_WhenCompressedChecksumInvalid_ThrowsCorruptedPackException()
    {
        byte[] packedBytes = Pack(CreateSyntheticElf(is64: false, machine: 3));
        PackHeader.TryFind(packedBytes, out int headerOffset, out var header);
        Assert.NotNull(header);
        int compressedOffset = headerOffset + PackHeader.SerializedSize;
        packedBytes[compressedOffset] ^= 0x7F;
        using var input = TempInput.Create(packedBytes);
        using var output = TempOutput.Create();
        var options = new Options { Method = CompressionMethod.Nrv2bLE32, Level = 1 };
        var unpacker = PackMaster.FindUnpacker(input.File, options);

        Assert.Throws<CorruptedPackException>(() => unpacker.DoUnpack(output.File, options));
    }

    [Fact]
    public void ElfPacker_Unpack_WhenUncompressedLengthExceedsCodeSection_ThrowsCorruptedPackException()
    {
        byte[] packedBytes = Pack(CreateSyntheticElf(is64: false, machine: 3));
        PackHeader.TryFind(packedBytes, out int headerOffset, out var header);
        Assert.NotNull(header);
        header!.ULen = 0x200;
        Span<byte> headerBytes = stackalloc byte[PackHeader.SerializedSize];
        header.Write(headerBytes);
        headerBytes.CopyTo(packedBytes.AsSpan(headerOffset));
        using var input = TempInput.Create(packedBytes);
        using var output = TempOutput.Create();
        var options = new Options { Method = CompressionMethod.Nrv2bLE32, Level = 1 };
        var unpacker = PackMaster.FindUnpacker(input.File, options);

        Assert.Throws<CorruptedPackException>(() => unpacker.DoUnpack(output.File, options));
    }

    private static byte[] Pack(byte[] original)
    {
        using var input = TempInput.Create(original);
        using var packed = TempOutput.Create();
        var options = new Options { Method = CompressionMethod.Nrv2bLE32, Level = 1 };
        var packer = PackMaster.FindPacker(input.File, options);
        packer.DoPack(packed.File, options);
        packed.DisposeFile();
        return File.ReadAllBytes(packed.Path);
    }

    private static byte[] CreateSyntheticElf(bool is64, ushort machine, bool isBigEndian = false)
    {
        byte[] data = new byte[0x400];
        data[0] = 0x7F;
        data[1] = (byte)'E';
        data[2] = (byte)'L';
        data[3] = (byte)'F';
        data[4] = is64 ? (byte)2 : (byte)1;
        data[5] = isBigEndian ? (byte)2 : (byte)1;
        data[6] = 1;

        if (is64)
            WriteElf64(data, machine, isBigEndian);
        else
            WriteElf32(data, machine, isBigEndian);

        byte[] text =
        [
            0x90, 0x90, 0xE8, 0x04, 0x00, 0x00, 0x00, 0x90,
            0x90, 0xE9, 0x02, 0x00, 0x00, 0x00, 0x90, 0xC3
        ];
        text.CopyTo(data.AsSpan(0x100));

        byte[] shstrtab = "\0.text\0.shstrtab\0"u8.ToArray();
        shstrtab.CopyTo(data.AsSpan(0x300));
        return data;
    }

    private static void WriteElf32(byte[] data, ushort machine, bool isBigEndian)
    {
        WriteUInt16(data.AsSpan(16), 2, isBigEndian);
        WriteUInt16(data.AsSpan(18), machine, isBigEndian);
        WriteUInt32(data.AsSpan(20), 1, isBigEndian);
        WriteUInt32(data.AsSpan(24), 0x08048100, isBigEndian);
        WriteUInt32(data.AsSpan(32), 0x180, isBigEndian);
        WriteUInt16(data.AsSpan(40), 52, isBigEndian);
        WriteUInt16(data.AsSpan(46), 40, isBigEndian);
        WriteUInt16(data.AsSpan(48), 3, isBigEndian);
        WriteUInt16(data.AsSpan(50), 2, isBigEndian);

        int text = 0x180 + 40;
        WriteUInt32(data.AsSpan(text), 1, isBigEndian);
        WriteUInt32(data.AsSpan(text + 4), 1, isBigEndian);
        WriteUInt32(data.AsSpan(text + 8), 0x6, isBigEndian);
        WriteUInt32(data.AsSpan(text + 12), 0x08048100, isBigEndian);
        WriteUInt32(data.AsSpan(text + 16), 0x100, isBigEndian);
        WriteUInt32(data.AsSpan(text + 20), 0x40, isBigEndian);

        int names = 0x180 + 80;
        WriteUInt32(data.AsSpan(names), 7, isBigEndian);
        WriteUInt32(data.AsSpan(names + 4), 3, isBigEndian);
        WriteUInt32(data.AsSpan(names + 16), 0x300, isBigEndian);
        WriteUInt32(data.AsSpan(names + 20), 0x11, isBigEndian);
    }

    private static void WriteElf64(byte[] data, ushort machine, bool isBigEndian)
    {
        WriteUInt16(data.AsSpan(16), 2, isBigEndian);
        WriteUInt16(data.AsSpan(18), machine, isBigEndian);
        WriteUInt32(data.AsSpan(20), 1, isBigEndian);
        WriteUInt64(data.AsSpan(24), 0x400100, isBigEndian);
        WriteUInt64(data.AsSpan(40), 0x180, isBigEndian);
        WriteUInt16(data.AsSpan(52), 64, isBigEndian);
        WriteUInt16(data.AsSpan(58), 64, isBigEndian);
        WriteUInt16(data.AsSpan(60), 3, isBigEndian);
        WriteUInt16(data.AsSpan(62), 2, isBigEndian);

        int text = 0x180 + 64;
        WriteUInt32(data.AsSpan(text), 1, isBigEndian);
        WriteUInt32(data.AsSpan(text + 4), 1, isBigEndian);
        WriteUInt64(data.AsSpan(text + 8), 0x6, isBigEndian);
        WriteUInt64(data.AsSpan(text + 16), 0x400100, isBigEndian);
        WriteUInt64(data.AsSpan(text + 24), 0x100, isBigEndian);
        WriteUInt64(data.AsSpan(text + 32), 0x40, isBigEndian);

        int names = 0x180 + 128;
        WriteUInt32(data.AsSpan(names), 7, isBigEndian);
        WriteUInt32(data.AsSpan(names + 4), 3, isBigEndian);
        WriteUInt64(data.AsSpan(names + 24), 0x300, isBigEndian);
        WriteUInt64(data.AsSpan(names + 32), 0x11, isBigEndian);
    }

    private static void WriteUInt16(Span<byte> destination, ushort value, bool isBigEndian)
    {
        if (isBigEndian)
            BinaryPrimitives.WriteUInt16BigEndian(destination, value);
        else
            BinaryPrimitives.WriteUInt16LittleEndian(destination, value);
    }

    private static void WriteUInt32(Span<byte> destination, uint value, bool isBigEndian)
    {
        if (isBigEndian)
            BinaryPrimitives.WriteUInt32BigEndian(destination, value);
        else
            BinaryPrimitives.WriteUInt32LittleEndian(destination, value);
    }

    private static void WriteUInt64(Span<byte> destination, ulong value, bool isBigEndian)
    {
        if (isBigEndian)
            BinaryPrimitives.WriteUInt64BigEndian(destination, value);
        else
            BinaryPrimitives.WriteUInt64LittleEndian(destination, value);
    }

    private sealed class TempInput : IDisposable
    {
        private readonly bool _delete;

        private TempInput(string path, bool delete)
        {
            Path = path;
            _delete = delete;
            File = new InputFile(path);
        }

        public string Path { get; }
        public InputFile File { get; }

        public static TempInput Create(byte[] data)
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"upxsharp-elf-{Guid.NewGuid():N}.bin");
            System.IO.File.WriteAllBytes(path, data);
            return new TempInput(path, delete: true);
        }

        public static TempInput OpenExisting(string path)
            => new(path, delete: false);

        public void Dispose()
        {
            File.Dispose();
            if (_delete)
                System.IO.File.Delete(Path);
        }
    }

    private sealed class TempOutput : IDisposable
    {
        private TempOutput(string path)
        {
            Path = path;
            _file = new OutputFile(path, overwrite: true);
        }

        public string Path { get; }
        public OutputFile File => _file ?? throw new ObjectDisposedException(nameof(TempOutput));
        private OutputFile? _file;

        public static TempOutput Create()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"upxsharp-elf-{Guid.NewGuid():N}.bin");
            return new TempOutput(path);
        }

        public void Dispose()
        {
            DisposeFile();
            System.IO.File.Delete(Path);
        }

        public void DisposeFile()
        {
            _file?.Dispose();
            _file = null;
        }
    }
}
