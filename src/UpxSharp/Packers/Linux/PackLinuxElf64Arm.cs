using UpxSharp.Core;
using UpxSharp.IO;

namespace UpxSharp.Packers.Linux;

public sealed class PackLinuxElf64Arm : PackLinuxElf
{
    public PackLinuxElf64Arm(InputFile file, Options options) : base(file, options) { }

    public override UpxFormat GetFormat() => UpxFormat.LinuxElfArm64LE;
    public override string GetName() => "linux/arm64";
    public override byte GetVersion() => 38;
    public override bool IsBigEndian => false;

    public override bool? CanPack()
        => IsElfFile(ELFCLASS64, EM_AARCH64)
            ? IsAlreadyPacked() ? null : true
            : false;

    public override bool? CanUnpack()
    {
        if (!IsElfFile(ELFCLASS64, EM_AARCH64)) return false;
        return HasPackHeader() || HasOfficialPackHeader();
    }

    public override void DoPack(OutputFile output, Options options)
        => DoPackElfFile(ELFCLASS64, EM_AARCH64, output, options);

    public override void DoUnpack(OutputFile output, Options options)
        => DoUnpackElfFile(ELFCLASS64, EM_AARCH64, output, options);
}
