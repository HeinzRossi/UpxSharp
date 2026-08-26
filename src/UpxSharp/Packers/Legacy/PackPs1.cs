using UpxSharp.Core;
using UpxSharp.IO;

namespace UpxSharp.Packers.Legacy;

public sealed class PackPs1 : LegacyScaffoldPacker
{
    private const int PayloadOffset = 0x800;

    public PackPs1(InputFile file, Options options) : base(file, options) { }

    public override UpxFormat GetFormat() => UpxFormat.Ps1Exe;
    public override string GetName() => "ps1/exe";
    public override byte GetVersion() => 90;
    public override bool IsBigEndian => false;

    protected override bool IsLegacyFile()
    {
        if (File.Length <= PayloadOffset) return false;
        Span<byte> magic = stackalloc byte[8];
        File.ReadExact(magic, 0);
        return magic.SequenceEqual("PS-X EXE"u8);
    }

    protected override PayloadInfo ReadPayloadInfo()
    {
        if (!IsLegacyFile())
            throw new CantPackException("PS1 scaffold inválido");

        uint size = checked((uint)(File.Length - PayloadOffset));
        if (size == 0)
            throw new CantPackException("Payload PS1 scaffold vazio");

        return new PayloadInfo(PayloadOffset, size);
    }
}
