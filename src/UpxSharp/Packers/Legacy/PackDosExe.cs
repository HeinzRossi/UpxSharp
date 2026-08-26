using UpxSharp.Core;
using UpxSharp.IO;
using System.Buffers.Binary;

namespace UpxSharp.Packers.Legacy;

public sealed class PackDosExe : LegacyScaffoldPacker
{
    private const int PayloadOffsetField = 0x20;
    private const int PayloadSizeField = 0x24;
    private const int MinimumPayloadOffset = 0x40;

    public PackDosExe(InputFile file, Options options) : base(file, options) { }

    public override UpxFormat GetFormat() => UpxFormat.DosExe;
    public override string GetName() => "dos/exe";
    public override byte GetVersion() => 3;
    public override bool IsBigEndian => false;

    protected override bool IsLegacyFile()
    {
        if (File.Length < MinimumPayloadOffset) return false;
        Span<byte> magic = stackalloc byte[2];
        File.ReadExact(magic, 0);
        return magic[0] == (byte)'M' && magic[1] == (byte)'Z';
    }

    protected override PayloadInfo ReadPayloadInfo()
    {
        try
        {
            return ReadOffsetSizeFields(PayloadOffsetField, PayloadSizeField, MinimumPayloadOffset);
        }
        catch (CantPackException)
        {
            return ReadMzHeaderPayloadInfo();
        }
        catch (EndOfStreamException)
        {
            return ReadMzHeaderPayloadInfo();
        }
    }

    private PayloadInfo ReadMzHeaderPayloadInfo()
    {
        Span<byte> header = stackalloc byte[0x1C];
        File.ReadExact(header, 0);

        ushort bytesInLastPage = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(0x02, 2));
        ushort pageCount = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(0x04, 2));
        ushort headerParagraphs = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(0x08, 2));

        uint payloadOffset = checked((uint)headerParagraphs * 16u);
        if (payloadOffset < MinimumPayloadOffset || payloadOffset >= File.Length)
            throw new CantPackException("Payload DOS EXE MZ inválido");

        uint imageSize = pageCount == 0
            ? (uint)File.Length
            : checked((uint)(pageCount - 1) * 512u + (bytesInLastPage == 0 ? 512u : bytesInLastPage));
        if (imageSize == 0 || imageSize > File.Length)
            imageSize = (uint)File.Length;

        if (imageSize <= payloadOffset)
            throw new CantPackException("Payload DOS EXE MZ vazio");

        return new PayloadInfo(payloadOffset, imageSize - payloadOffset);
    }
}
