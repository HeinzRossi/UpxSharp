using UpxSharp.Core;
using UpxSharp.IO;

namespace UpxSharp.Packers.Linux;

public sealed class PackLinuxElf32Mips : PackLinuxElf
{
    public PackLinuxElf32Mips(InputFile file, Options options) : base(file, options) { }

    public override UpxFormat GetFormat() => UpxFormat.LinuxElfMipsel;
    public override string GetName() => "linux/mipsel";
    public override byte GetVersion() => 40;
    public override bool IsBigEndian => false;

    public override bool? CanPack()
        => IsElfFile(ELFCLASS32, EM_MIPS)
            ? IsAlreadyPacked() ? null : true
            : false;

    public override bool? CanUnpack()
    {
        if (!IsElfFile(ELFCLASS32, EM_MIPS)) return false;
        return HasPackHeader() || HasOfficialPackHeader();
    }

    public override void DoPack(OutputFile output, Options options)
        => DoPackElfFile(ELFCLASS32, EM_MIPS, output, options);

    public override void DoUnpack(OutputFile output, Options options)
        => DoUnpackElfFile(ELFCLASS32, EM_MIPS, output, options);
}
