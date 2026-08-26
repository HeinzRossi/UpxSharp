using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using UpxSharp.Core;
using UpxSharp.IO;
using UpxSharp.Packers;
using UpxSharp.Packers.Legacy;
using Xunit;

namespace UpxSharp.Tests;

public class VmlinuzHandlerTests
{
    private const int PayloadOffset = 0x300;
    private const int PayloadSize = 0x40;

    [Fact]
    public void PackMaster_FindPacker_DetectsSyntheticVmlinuz()
    {
        using var input = TempInput.Create(CreateSyntheticVmlinuz());

        var packer = PackMaster.FindPacker(input.File, new Options { Method = CompressionMethod.Nrv2bLE32, Level = 1 });

        Assert.IsType<PackVmlinuzI386>(packer);
    }

    [Fact]
    public void VmlinuzPacker_PackAndUnpack_RestoresOriginalBytes()
    {
        byte[] original = CreateSyntheticVmlinuz();
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

    [Fact]
    public void VmlinuzPacker_WithBootProtocolPayloadFallback_RestoresOriginalBytes()
    {
        byte[] original = CreateBootProtocolVmlinuz();
        using var input = TempInput.Create(original);
        using var packed = TempOutput.Create();
        using var unpacked = TempOutput.Create();
        var options = new Options { Method = CompressionMethod.Nrv2bLE32, Level = 1 };

        var packer = PackMaster.FindPacker(input.File, options);
        Assert.IsType<PackVmlinuzI386>(packer);
        packer.DoPack(packed.File, options);
        packed.DisposeFile();

        using var packedInput = TempInput.OpenExisting(packed.Path);
        var unpacker = PackMaster.FindUnpacker(packedInput.File, options);
        unpacker.DoUnpack(unpacked.File, options);
        unpacked.DisposeFile();

        Assert.Equal(original, File.ReadAllBytes(unpacked.Path));
    }

    [Fact]
    public void PackMaster_FindUnpacker_WhenVmlinuzPacked_ReturnsExpectedPacker()
    {
        byte[] packedBytes = Pack(CreateSyntheticVmlinuz());
        using var input = TempInput.Create(packedBytes);

        var unpacker = PackMaster.FindUnpacker(input.File, new Options { Method = CompressionMethod.Nrv2bLE32, Level = 1 });

        Assert.IsType<PackVmlinuzI386>(unpacker);
    }

    [Fact]
    public void PackMaster_FindUnpacker_WhenVmlinuzNotPacked_ThrowsNotPackedException()
    {
        using var input = TempInput.Create(CreateSyntheticVmlinuz());

        Assert.Throws<NotPackedException>(() => PackMaster.FindUnpacker(input.File, new Options()));
    }

    [Fact]
    public void VmlinuzPacker_Unpack_WhenPayloadTruncated_ThrowsCorruptedPackException()
    {
        byte[] packedBytes = Pack(CreateSyntheticVmlinuz());
        byte[] truncated = packedBytes[..^1];
        using var input = TempInput.Create(truncated);
        using var output = TempOutput.Create();
        var options = new Options { Method = CompressionMethod.Nrv2bLE32, Level = 1 };
        var unpacker = PackMaster.FindUnpacker(input.File, options);

        Assert.Throws<CorruptedPackException>(() => unpacker.DoUnpack(output.File, options));
    }

    [Fact]
    public void VmlinuzPacker_Unpack_WhenCompressedChecksumInvalid_ThrowsCorruptedPackException()
    {
        byte[] packedBytes = Pack(CreateSyntheticVmlinuz());
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
    public void VmlinuzPacker_Unpack_WhenUncompressedLengthExceedsPayload_ThrowsCorruptedPackException()
    {
        byte[] packedBytes = Pack(CreateSyntheticVmlinuz());
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

    private static byte[] CreateSyntheticVmlinuz()
    {
        byte[] data = new byte[0x400];
        data[0] = (byte)'M';
        data[1] = (byte)'Z';
        "HdrS"u8.CopyTo(data.AsSpan(0x202));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x210), PayloadOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x214), PayloadSize);

        byte[] payload =
        [
            0x90, 0x90, 0xE8, 0x04, 0x00, 0x00, 0x00, 0x90,
            0x90, 0xE9, 0x02, 0x00, 0x00, 0x00, 0x90, 0xC3
        ];
        payload.CopyTo(data.AsSpan(PayloadOffset));
        return data;
    }

    private static byte[] CreateBootProtocolVmlinuz()
    {
        byte[] data = new byte[0xB00];
        data[0] = (byte)'M';
        data[1] = (byte)'Z';
        data[0x1F1] = 0;
        "HdrS"u8.CopyTo(data.AsSpan(0x202));

        byte[] payload =
        [
            0x90, 0x90, 0xE8, 0x04, 0x00, 0x00, 0x00, 0x90,
            0x90, 0xE9, 0x02, 0x00, 0x00, 0x00, 0x90, 0xC3
        ];
        payload.CopyTo(data.AsSpan(0xA00));
        return data;
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
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"upxsharp-vmlinuz-{Guid.NewGuid():N}.bin");
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
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"upxsharp-vmlinuz-{Guid.NewGuid():N}.bin");
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
