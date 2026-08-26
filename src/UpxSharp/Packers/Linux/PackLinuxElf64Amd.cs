using UpxSharp.Core;
using UpxSharp.IO;

namespace UpxSharp.Packers.Linux;

public sealed class PackLinuxElf64Amd : PackLinuxElf
{
    public PackLinuxElf64Amd(InputFile file, Options options) : base(file, options) { }

    public override UpxFormat GetFormat() => UpxFormat.LinuxElfAmd64;
    public override string GetName() => "linux/amd64";
    public override byte GetVersion() => 24;
    public override bool IsBigEndian => false;

    public override bool? CanPack()
        => IsElfFile(ELFCLASS64, EM_X86_64)
            ? IsAlreadyPacked() ? null : true
            : false;

    public override bool? CanUnpack()
    {
        if (!IsElfFile(ELFCLASS64, EM_X86_64)) return false;
        return HasPackHeader() || HasOfficialPackHeader();
    }

    public override void DoPack(OutputFile output, Options options)
        => DoPackElfFile(ELFCLASS64, EM_X86_64, output, options);

    public override void DoUnpack(OutputFile output, Options options)
        => DoUnpackElfFile(ELFCLASS64, EM_X86_64, output, options);
}
