using System.Buffers.Binary;
using System.Text;
using UpxSharp.Core;

namespace UpxSharp.Official;

public sealed record OfficialUpxBlock(
    int HeaderOffset,
    int PayloadOffset,
    uint UncompressedSize,
    uint CompressedSize,
    CompressionMethod Method,
    byte FilterId,
    byte FilterCto,
    byte SegmentSequence);

public sealed record OfficialElfProgramHeader(
    uint Type,
    ulong Offset,
    ulong VirtualAddress,
    ulong FileSize,
    ulong MemorySize,
    uint Flags,
    ulong Align);

public sealed record OfficialUpxMetadata(
    UpxFormat Format,
    string Container,
    int SignatureOffset,
    string Reason,
    uint OriginalFileSize = 0,
    uint BlockSize = 0,
    bool IsBigEndian = false,
    int LoaderInfoOffset = -1,
    int ProgramInfoOffset = -1,
    int BlocksOffset = -1,
    byte PackedHeaderVersion = 0,
    byte PackedHeaderFormat = 0,
    bool IsCompleteBlockMap = false,
    byte OriginalElfClass = 0,
    ushort OriginalMachine = 0,
    ulong OriginalEntry = 0,
    bool HasOriginalElfHeaders = false,
    IReadOnlyList<OfficialElfProgramHeader>? OriginalProgramHeaders = null,
    IReadOnlyList<OfficialElfProgramHeader>? PackedProgramHeaders = null,
    bool IsCBaseLayout = false,
    int LoaderEndOffset = -1,
    IReadOnlyList<OfficialUpxBlock>? Blocks = null,
    OfficialPackHeader? PackHeader = null);

public static class OfficialUpxDetector
{
    public static bool TryDetect(ReadOnlySpan<byte> data, UpxFormat expectedFormat, out OfficialUpxMetadata? metadata)
    {
        metadata = null;
        if (data.Length < 64)
            return false;

        return expectedFormat switch
        {
            UpxFormat.Win32Pe or UpxFormat.Win64Pe => TryDetectPe(data, expectedFormat, out metadata),
            UpxFormat.LinuxElfi386
                or UpxFormat.LinuxElfAmd64
                or UpxFormat.LinuxElfArmLE
                or UpxFormat.LinuxElfArm64LE
                or UpxFormat.LinuxElfMipsel
                or UpxFormat.LinuxElfMips
                or UpxFormat.LinuxElf32Ppc
                or UpxFormat.LinuxElf64Ppc
                or UpxFormat.LinuxElfRiscv64 => TryDetectElf(data, expectedFormat, out metadata),
            UpxFormat.DosExe => TryDetectDosExe(data, expectedFormat, out metadata),
            _ => false,
        };
    }

    public static void ThrowUnsupported(OfficialUpxMetadata metadata)
        => throw new OfficialUpxUnsupportedException(
            $"{metadata.Format} ({metadata.Container}) detectado em offset 0x{metadata.SignatureOffset:X}; parser de payload/stub oficial pendente");

    private static bool TryDetectPe(ReadOnlySpan<byte> data, UpxFormat expectedFormat, out OfficialUpxMetadata? metadata)
    {
        metadata = null;
        if (data.Length < 0x40 || !data[..2].SequenceEqual("MZ"u8))
            return false;

        int peOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[0x3C..]);
        if (peOffset < 0x40 || peOffset + 24 > data.Length || !data.Slice(peOffset, 4).SequenceEqual("PE\0\0"u8))
            return false;

        ushort sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(data[(peOffset + 6)..]);
        ushort optionalHeaderSize = BinaryPrimitives.ReadUInt16LittleEndian(data[(peOffset + 20)..]);
        int sectionTable = peOffset + 24 + optionalHeaderSize;
        if (sectionCount == 0 || sectionTable < 0 || sectionTable + sectionCount * 40 > data.Length)
            return false;

        bool hasUpx0 = false;
        bool hasUpx1 = false;
        int signatureOffset = -1;
        for (int i = 0; i < sectionCount; i++)
        {
            int offset = sectionTable + i * 40;
            string name = ReadFixedString(data.Slice(offset, 8));
            if (name == "UPX0")
            {
                hasUpx0 = true;
                signatureOffset = offset;
            }
            else if (name == "UPX1")
            {
                hasUpx1 = true;
                signatureOffset = signatureOffset < 0 ? offset : signatureOffset;
            }
        }

        if (!hasUpx0 || !hasUpx1)
            return false;

        metadata = new OfficialUpxMetadata(expectedFormat, "PE", signatureOffset, "PE section table contains UPX0/UPX1");
        return true;
    }

    private static bool TryDetectElf(ReadOnlySpan<byte> data, UpxFormat expectedFormat, out OfficialUpxMetadata? metadata)
    {
        metadata = null;
        if (!data[..4].SequenceEqual("\u007FELF"u8))
            return false;

        byte elfClass = data[4];
        bool bigEndian = data[5] == 2;
        if (data[5] is not (1 or 2))
            return false;

        ulong sectionTableOffset;
        ushort sectionCount;
        if (elfClass == 1)
        {
            if (data.Length < 52) return false;
            sectionTableOffset = ReadUInt32(data[32..36], bigEndian);
            sectionCount = ReadUInt16(data[48..50], bigEndian);
        }
        else if (elfClass == 2)
        {
            if (data.Length < 64) return false;
            sectionTableOffset = ReadUInt64(data[40..48], bigEndian);
            sectionCount = ReadUInt16(data[60..62], bigEndian);
        }
        else
        {
            return false;
        }

        int signatureOffset = IndexOf(data, "UPX!"u8);
        int infoOffset = IndexOf(data, "$Info:"u8);
        if (signatureOffset < 0 || infoOffset < 0)
            return false;

        if (sectionTableOffset != 0 || sectionCount != 0)
            return false;

        metadata = new OfficialUpxMetadata(expectedFormat, "ELF", signatureOffset, "ELF has UPX loader signatures and no section table");
        return true;
    }

    private static bool TryDetectDosExe(ReadOnlySpan<byte> data, UpxFormat expectedFormat, out OfficialUpxMetadata? metadata)
    {
        metadata = null;
        if (!data[..2].SequenceEqual("MZ"u8))
            return false;

        int signatureOffset = IndexOf(data, "UPX!"u8);
        int infoOffset = IndexOf(data, "$Info:"u8);
        if (signatureOffset < 0 || infoOffset < 0)
            return false;

        // Restored UPX DOS binaries contain UPX strings too; the packed DOS EXE keeps
        // the loader metadata near the MZ header, while restored binaries do not.
        if (signatureOffset > 0x10000 || infoOffset > 0x10000)
            return false;

        metadata = new OfficialUpxMetadata(expectedFormat, "DOS EXE", signatureOffset, "MZ executable contains UPX loader marker near the header");
        return true;
    }

    private static int IndexOf(ReadOnlySpan<byte> data, ReadOnlySpan<byte> needle)
    {
        for (int i = 0; i <= data.Length - needle.Length; i++)
        {
            if (data.Slice(i, needle.Length).SequenceEqual(needle))
                return i;
        }

        return -1;
    }

    private static string ReadFixedString(ReadOnlySpan<byte> data)
    {
        int end = 0;
        while (end < data.Length && data[end] != 0)
            end++;
        return Encoding.ASCII.GetString(data[..end]);
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, bool bigEndian)
        => bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(data) : BinaryPrimitives.ReadUInt16LittleEndian(data);

    private static uint ReadUInt32(ReadOnlySpan<byte> data, bool bigEndian)
        => bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(data) : BinaryPrimitives.ReadUInt32LittleEndian(data);

    private static ulong ReadUInt64(ReadOnlySpan<byte> data, bool bigEndian)
        => bigEndian ? BinaryPrimitives.ReadUInt64BigEndian(data) : BinaryPrimitives.ReadUInt64LittleEndian(data);
}
