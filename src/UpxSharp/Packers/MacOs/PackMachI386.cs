using UpxSharp.Core;
using UpxSharp.IO;

namespace UpxSharp.Packers.MacOs;

public sealed class PackMachI386 : PackMachBase
{
    public PackMachI386(InputFile file, Options options) : base(file, options) { }

    public override UpxFormat GetFormat() => UpxFormat.MachI386;
    public override string GetName() => "macho/i386";
    public override byte GetVersion() => 51;
    public override bool IsBigEndian => false;

    public override bool? CanPack()
        => IsMachFile(MH_MAGIC, CPU_TYPE_I386)
            ? IsAlreadyPacked() ? null : true
            : false;

    public override bool? CanUnpack()
    {
        if (!IsMachFile(MH_MAGIC, CPU_TYPE_I386)) return false;
        return HasPackHeader() || HasOfficialPackHeader();
    }

    public override void DoPack(OutputFile output, Options options)
        => DoPackMachFile(MH_MAGIC, CPU_TYPE_I386, output, options);

    public override void DoUnpack(OutputFile output, Options options)
        => DoUnpackMachFile(MH_MAGIC, CPU_TYPE_I386, output, options);
}
