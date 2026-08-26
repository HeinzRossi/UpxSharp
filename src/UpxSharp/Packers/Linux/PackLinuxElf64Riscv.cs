using UpxSharp.Core;
using UpxSharp.IO;

namespace UpxSharp.Packers.Linux;

public sealed class PackLinuxElf64Riscv : PackLinuxElf
{
    public PackLinuxElf64Riscv(InputFile file, Options options) : base(file, options) { }

    public override UpxFormat GetFormat() => UpxFormat.LinuxElfRiscv64;
    public override string GetName() => "linux/riscv64";
    public override byte GetVersion() => 49;
    public override bool IsBigEndian => false;

    public override bool? CanPack()
        => IsElfFile(ELFCLASS64, EM_RISCV)
            ? IsAlreadyPacked() ? null : true
            : false;

    public override bool? CanUnpack()
    {
        if (!IsElfFile(ELFCLASS64, EM_RISCV)) return false;
        return HasPackHeader() || HasOfficialPackHeader();
    }

    public override void DoPack(OutputFile output, Options options)
        => DoPackElfFile(ELFCLASS64, EM_RISCV, output, options);

    public override void DoUnpack(OutputFile output, Options options)
        => DoUnpackElfFile(ELFCLASS64, EM_RISCV, output, options);
}
