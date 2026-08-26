using UpxSharp.Core;
using UpxSharp.IO;

namespace UpxSharp.Packers.MacOs;

public sealed class PackMachAMD64 : PackMachBase
{
    public PackMachAMD64(InputFile file, Options options) : base(file, options) { }

    public override UpxFormat GetFormat() => UpxFormat.MachAMD64;
    public override string GetName() => "macho/amd64";
    public override byte GetVersion() => 52;
    public override bool IsBigEndian => false;

    public override bool? CanPack()
        => IsMachFile(MH_MAGIC_64, CPU_TYPE_X86_64)
            ? IsAlreadyPacked() ? null : true
            : false;

    public override bool? CanUnpack()
    {
        if (!IsMachFile(MH_MAGIC_64, CPU_TYPE_X86_64)) return false;
        return HasPackHeader() || HasOfficialPackHeader();
    }

    public override void DoPack(OutputFile output, Options options)
        => DoPackMachFile(MH_MAGIC_64, CPU_TYPE_X86_64, output, options);

    public override void DoUnpack(OutputFile output, Options options)
        => DoUnpackMachFile(MH_MAGIC_64, CPU_TYPE_X86_64, output, options);
}
