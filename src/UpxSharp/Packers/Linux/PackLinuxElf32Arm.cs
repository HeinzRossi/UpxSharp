using UpxSharp.Core;
using UpxSharp.IO;

namespace UpxSharp.Packers.Linux;

public sealed class PackLinuxElf32Arm : PackLinuxElf
{
    public PackLinuxElf32Arm(InputFile file, Options options) : base(file, options) { }

    public override UpxFormat GetFormat() => UpxFormat.LinuxElfArmLE;
    public override string GetName() => "linux/arm";
    public override byte GetVersion() => 30;
    public override bool IsBigEndian => false;

    public override bool? CanPack()
        => IsElfFile(ELFCLASS32, EM_ARM)
            ? IsAlreadyPacked() ? null : true
            : false;

    public override bool? CanUnpack()
    {
        if (!IsElfFile(ELFCLASS32, EM_ARM)) return false;
        return HasPackHeader() || HasOfficialPackHeader();
    }

    public override void DoPack(OutputFile output, Options options)
        => DoPackElfFile(ELFCLASS32, EM_ARM, output, options);

    public override void DoUnpack(OutputFile output, Options options)
        => DoUnpackElfFile(ELFCLASS32, EM_ARM, output, options);
}
