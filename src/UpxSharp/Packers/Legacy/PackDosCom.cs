using UpxSharp.Core;
using UpxSharp.IO;

namespace UpxSharp.Packers.Legacy;

public sealed class PackDosCom : LegacyScaffoldPacker
{
    private const int PayloadOffsetField = 0x10;
    private const int PayloadSizeField = 0x14;
    private const int MinimumPayloadOffset = 0x20;
    private const int MaximumComSize = 65535;

    public PackDosCom(InputFile file, Options options) : base(file, options) { }

    public override UpxFormat GetFormat() => UpxFormat.DosCom;
    public override string GetName() => "dos/com";
    public override byte GetVersion() => 1;
    public override bool IsBigEndian => false;

    protected override bool IsLegacyFile()
    {
        if (File.Length <= 0)
            return false;

        Span<byte> prefix = stackalloc byte[4];
        int prefixLength = File.Read(prefix, 0);
        if (prefixLength >= 4 && prefix.SequenceEqual("COM!"u8))
            return true;

        if (TryReadDosComTrailer(out _))
            return true;

        if (File.Length > MaximumComSize ||
            !string.Equals(Path.GetExtension(File.Name), ".com", StringComparison.OrdinalIgnoreCase))
            return false;

        return prefixLength < 2 || prefix[0] != (byte)'M' || prefix[1] != (byte)'Z';
    }

    protected override PayloadInfo ReadPayloadInfo()
    {
        Span<byte> magic = stackalloc byte[4];
        if (File.Length >= magic.Length)
        {
            File.ReadExact(magic, 0);
            if (magic.SequenceEqual("COM!"u8))
                return ReadOffsetSizeFields(PayloadOffsetField, PayloadSizeField, MinimumPayloadOffset);
        }

        if (TryReadDosComTrailer(out var header))
            return new PayloadInfo(0, header!.UFileSize);

        if (File.Length is <= 0 or > MaximumComSize)
            throw new CantPackException("Tamanho DOS COM invalido");

        return new PayloadInfo(0, checked((uint)File.Length));
    }

    private bool TryReadDosComTrailer(out PackHeader? header)
    {
        header = null;
        if (File.Length < PackHeader.SerializedSize || File.Length > int.MaxValue)
            return false;

        using var data = MemBuffer.Alloc((int)File.Length);
        File.ReadExact(data.Span, 0);
        return PackHeader.TryFindValidTrailer(data.Span, out _, out header) &&
               header?.Format == UpxFormat.DosCom &&
               header.UFileSize is > 0 and <= MaximumComSize;
    }
}
