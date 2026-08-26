using System.Buffers.Binary;
using UpxSharp.Core;
using UpxSharp.IO;

namespace UpxSharp.Packers.Legacy;

public sealed class PackAtari : LegacyScaffoldPacker
{
    private const int PayloadOffsetField = 0x20;
    private const int PayloadSizeField = 0x24;
    private const int MinimumPayloadOffset = 0x40;
    private const int TosHeaderSize = 28;

    public PackAtari(InputFile file, Options options) : base(file, options) { }

    public override UpxFormat GetFormat() => UpxFormat.AtariTos;
    public override string GetName() => "atari/tos";
    public override byte GetVersion() => 80;
    public override bool IsBigEndian => true;

    protected override bool IsLegacyFile()
    {
        if (File.Length < TosHeaderSize) return false;
        Span<byte> magic = stackalloc byte[2];
        File.ReadExact(magic, 0);
        return magic[0] == 0x60 && magic[1] == 0x1A;
    }

    protected override PayloadInfo ReadPayloadInfo()
    {
        Span<byte> header = stackalloc byte[TosHeaderSize];
        File.ReadExact(header, 0);
        uint textSize = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(2, 4));
        uint dataSize = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(6, 4));
        if (textSize == 0 && dataSize == 0)
            return ReadOffsetSizeFields(PayloadOffsetField, PayloadSizeField, MinimumPayloadOffset);

        uint payloadSize;
        try
        {
            payloadSize = checked(textSize + dataSize);
        }
        catch (OverflowException)
        {
            throw new CantPackException("Tamanho Atari TOS invalido");
        }

        if (payloadSize == 0 || payloadSize > File.Length - TosHeaderSize)
            throw new CantPackException("Payload Atari TOS invalido");

        return new PayloadInfo(TosHeaderSize, payloadSize);
    }
}
