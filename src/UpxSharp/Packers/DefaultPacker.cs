using UpxSharp.Core;
using UpxSharp.IO;

namespace UpxSharp.Packers;

public sealed class DefaultPacker : Packer
{
    public DefaultPacker(InputFile file, Options options) : base(file, options) { }

    public override UpxFormat GetFormat() => UpxFormat.Win32Pe;
    public override string GetName() => "default";
    public override byte GetVersion() => 0;
    public override bool IsBigEndian => false;

    public override bool? CanPack() => false;
    public override bool? CanUnpack() => false;

    public override void DoPack(OutputFile output, Options options)
        => throw new NotImplementedException();

    public override void DoUnpack(OutputFile output, Options options)
        => throw new NotImplementedException();
}
