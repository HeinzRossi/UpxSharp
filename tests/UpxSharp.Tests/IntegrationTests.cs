using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UpxSharp.Core;
using UpxSharp.IO;
using UpxSharp.Packers;
using Xunit;

namespace UpxSharp.Tests;

public class IntegrationTests
{
    public static IEnumerable<object[]> ImplementedFormatFixtures()
    {
        yield return ["pe32", CreatePe(0x014C)];
        yield return ["pe64", CreatePe(0x8664)];
        yield return ["elf-i386", CreateElf(is64: false, machine: 3)];
        yield return ["elf-amd64", CreateElf(is64: true, machine: 62)];
        yield return ["elf-arm32", CreateElf(is64: false, machine: 40)];
        yield return ["elf-arm64", CreateElf(is64: true, machine: 183)];
        yield return ["elf-mips", CreateElf(is64: false, machine: 8)];
        yield return ["elf-mips-be", CreateElf(is64: false, machine: 8, isBigEndian: true)];
        yield return ["elf-ppc32", CreateElf(is64: false, machine: 20, isBigEndian: true)];
        yield return ["elf-ppc64", CreateElf(is64: true, machine: 21, isBigEndian: true)];
        yield return ["elf-riscv64", CreateElf(is64: true, machine: 243)];
        yield return ["mach-i386", CreateMach(is64: false)];
        yield return ["mach-amd64", CreateMach(is64: true)];
        yield return ["vmlinuz-i386", CreateVmlinuz()];
        yield return ["dos-exe", CreateDosExe()];
        yield return ["dos-com", CreateDosCom()];
        yield return ["atari-tos", CreateAtari()];
        yield return ["ps1-exe", CreatePs1()];
    }

    [Theory]
    [MemberData(nameof(ImplementedFormatFixtures))]
    public void PackMaster_PackThenUnpack_RestoresOriginalBytes(string name, byte[] original)
    {
        _ = name;
        using var input = TempFile.Create(original);
        using var packed = TempFile.CreateEmpty();
        using var unpacked = TempFile.CreateEmpty();
        var options = IsPeFixture(name)
            ? new Options { Method = CompressionMethod.Lzma, Level = 9 }
            : new Options { Method = CompressionMethod.Nrv2bLE32, Level = 1 };

        using (var inputFile = new InputFile(input.Path))
        using (var outputFile = new OutputFile(packed.Path, overwrite: true))
            PackMaster.FindPacker(inputFile, options).DoPack(outputFile, options);

        byte[] packedBytes = File.ReadAllBytes(packed.Path);
        if (IsPeFixture(name))
            Assert.True(packedBytes.Length < original.Length);
        else
            Assert.True(packedBytes.Length > original.Length);
        Assert.Contains("UPX!", Encoding.ASCII.GetString(packedBytes));

        using (var inputFile = new InputFile(packed.Path))
        using (var outputFile = new OutputFile(unpacked.Path, overwrite: true))
            PackMaster.FindUnpacker(inputFile, options).DoUnpack(outputFile, options);

        Assert.Equal(original, File.ReadAllBytes(unpacked.Path));
    }

    [Theory]
    [InlineData("pe")]
    public void CommandLineRunner_Test_PacksUnpacksAndCompares(string fixture)
    {
        using var input = TempFile.Create(CreateCliFixture(fixture));
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = CommandLineRunner.Run(["test", input.Path, "--lzma", "-9"], output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("OK:", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void CommandLineRunner_List_PrintsPackedAndUnpackedState()
    {
        using var input = TempFile.Create(CreateElf(is64: true, machine: 62));
        using var packed = TempFile.CreateEmpty();
        using var listOutput = new StringWriter();
        using var packedListOutput = new StringWriter();
        using var error = new StringWriter();
        var options = new Options { Method = CompressionMethod.Lzma, Level = 9 };

        int unpackedExitCode = CommandLineRunner.Run(["list", input.Path], listOutput, error);
        Assert.Equal(0, unpackedExitCode);
        Assert.Contains("unpacked", listOutput.ToString());

        using (var inputFile = new InputFile(input.Path))
        using (var outputFile = new OutputFile(packed.Path, overwrite: true))
            PackMaster.FindPacker(inputFile, options).DoPack(outputFile, options);

        int packedExitCode = CommandLineRunner.Run(["list", packed.Path], packedListOutput, error);
        Assert.Equal(0, packedExitCode);
        Assert.Contains("packed", packedListOutput.ToString());
    }

    [Theory]
    [InlineData("pe")]
    [InlineData("elf")]
    [InlineData("mach")]
    [InlineData("legacy")]
    public void PackMaster_Unpack_WhenCompressedPayloadCorrupted_ThrowsCorruptedPackException(string fixture)
    {
        byte[] original = CreateCliFixture(fixture);
        byte[] packedBytes = Pack(original);
        PackHeader.TryFind(packedBytes, out int headerOffset, out var header);
        Assert.NotNull(header);
        packedBytes[headerOffset + PackHeader.SerializedSize] ^= 0x7F;
        using var packed = TempFile.Create(packedBytes);
        using var output = TempFile.CreateEmpty();
        var options = IsPeFixture(fixture)
            ? new Options { Method = CompressionMethod.Lzma, Level = 9 }
            : new Options { Method = CompressionMethod.Nrv2bLE32, Level = 1 };

        using var inputFile = new InputFile(packed.Path);
        using var outputFile = new OutputFile(output.Path, overwrite: true);
        var unpacker = PackMaster.FindUnpacker(inputFile, options);

        Assert.Throws<CorruptedPackException>(() => unpacker.DoUnpack(outputFile, options));
    }

    private static byte[] Pack(byte[] original)
    {
        using var input = TempFile.Create(original);
        using var packed = TempFile.CreateEmpty();
        var options = new Options { Method = CompressionMethod.Lzma, Level = 9 };

        using (var inputFile = new InputFile(input.Path))
        using (var outputFile = new OutputFile(packed.Path, overwrite: true))
            PackMaster.FindPacker(inputFile, options).DoPack(outputFile, options);

        return File.ReadAllBytes(packed.Path);
    }

    private static byte[] CreateCliFixture(string fixture) => fixture switch
    {
        "pe" => CreatePe(0x014C),
        "elf" => CreateElf(is64: false, machine: 3),
        "mach" => CreateMach(is64: false),
        "legacy" => CreateDosExe(),
        _ => throw new ArgumentOutOfRangeException(nameof(fixture)),
    };

    private static byte[] CreatePe(ushort machine)
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
        CopyX86Payload(data.AsSpan(0x200));
        return data;
    }

    private static byte[] CreateElf(bool is64, ushort machine, bool isBigEndian = false)
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

        CopyX86Payload(data.AsSpan(0x100));
        "\0.text\0.shstrtab\0"u8.CopyTo(data.AsSpan(0x300));
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

    private static byte[] CreateMach(bool is64)
    {
        byte[] data = new byte[0x400];
        if (is64)
            WriteMach64(data);
        else
            WriteMach32(data);

        CopyX86Payload(data.AsSpan(0x200));
        return data;
    }

    private static void WriteMach32(byte[] data)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(data, 0xFEEDFACE);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 7);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20), 124);
        int segment = 28;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(segment), 0x1);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(segment + 4), 124);
        WriteFixedString(data.AsSpan(segment + 8, 16), "__TEXT");
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(segment + 24), 0x1000);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(segment + 28), 0x1000);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(segment + 36), 0x400);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(segment + 48), 1);
        int section = segment + 56;
        WriteFixedString(data.AsSpan(section, 16), "__text");
        WriteFixedString(data.AsSpan(section + 16, 16), "__TEXT");
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(section + 32), 0x1000);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(section + 36), 0x40);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(section + 40), 0x200);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(section + 56), 0x80000400);
    }

    private static void WriteMach64(byte[] data)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(data, 0xFEEDFACF);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 0x01000007);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20), 152);
        int segment = 32;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(segment), 0x19);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(segment + 4), 152);
        WriteFixedString(data.AsSpan(segment + 8, 16), "__TEXT");
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(segment + 24), 0x100000000);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(segment + 32), 0x1000);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(segment + 48), 0x400);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(segment + 64), 1);
        int section = segment + 72;
        WriteFixedString(data.AsSpan(section, 16), "__text");
        WriteFixedString(data.AsSpan(section + 16, 16), "__TEXT");
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(section + 32), 0x100000200);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(section + 40), 0x40);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(section + 48), 0x200);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(section + 64), 0x80000400);
    }

    private static byte[] CreateVmlinuz()
    {
        byte[] data = new byte[0x400];
        data[0] = (byte)'M';
        data[1] = (byte)'Z';
        "HdrS"u8.CopyTo(data.AsSpan(0x202));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x210), 0x300);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x214), 0x40);
        CopyX86Payload(data.AsSpan(0x300));
        return data;
    }

    private static byte[] CreateDosExe()
    {
        byte[] data = CreateLegacyPayload(0x400, 0x80);
        data[0] = (byte)'M';
        data[1] = (byte)'Z';
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x20), 0x80);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x24), 0x40);
        return data;
    }

    private static byte[] CreateDosCom()
    {
        byte[] data = CreateLegacyPayload(0x400, 0x80);
        "COM!"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x10), 0x80);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x14), 0x40);
        return data;
    }

    private static byte[] CreateAtari()
    {
        byte[] data = CreateLegacyPayload(0x400, 0x80);
        data[0] = 0x60;
        data[1] = 0x1A;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x20), 0x80);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x24), 0x40);
        return data;
    }

    private static byte[] CreatePs1()
    {
        byte[] data = CreateLegacyPayload(0x840, 0x800);
        "PS-X EXE"u8.CopyTo(data);
        return data;
    }

    private static byte[] CreateLegacyPayload(int fileSize, int payloadOffset)
    {
        byte[] data = new byte[fileSize];
        CopyX86Payload(data.AsSpan(payloadOffset));
        return data;
    }

    private static void CopyX86Payload(Span<byte> destination)
    {
        byte[] payload =
        [
            0x90, 0x90, 0xE8, 0x04, 0x00, 0x00, 0x00, 0x90,
            0x90, 0xE9, 0x02, 0x00, 0x00, 0x00, 0x90, 0xC3
        ];
        payload.CopyTo(destination);
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

    private static void WriteFixedString(Span<byte> destination, string value)
    {
        destination.Clear();
        Encoding.ASCII.GetBytes(value).CopyTo(destination);
    }

    private static bool IsPeFixture(string name)
        => name is "pe" or "pe32" or "pe64";

    private sealed class TempFile : IDisposable
    {
        private TempFile(string path) => Path = path;

        public string Path { get; }

        public static TempFile Create(byte[] data)
        {
            var file = CreateEmpty();
            File.WriteAllBytes(file.Path, data);
            return file;
        }

        public static TempFile CreateEmpty()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"upxsharp-integration-{Guid.NewGuid():N}.bin");
            return new TempFile(path);
        }

        public void Dispose()
        {
            if (File.Exists(Path))
                File.Delete(Path);
        }
    }
}
