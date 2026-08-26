using UpxSharp.Core;
using UpxSharp.IO;

namespace UpxSharp.Packers.Linux;

public sealed class PackLinuxElf32Ppc : PackLinuxElf
{
    public PackLinuxElf32Ppc(InputFile file, Options options) : base(file, options) { }

    public override UpxFormat GetFormat() => UpxFormat.LinuxElf32Ppc;
    public override string GetName() => "linux/ppc32";
    public override byte GetVersion() => 41;
    public override bool IsBigEndian => true;

    public override bool? CanPack()
        => IsElfFile(ELFCLASS32, EM_PPC, ELFDATA2MSB)
            ? IsAlreadyPacked() ? null : true
            : false;

    public override bool? CanUnpack()
    {
        if (!IsElfFile(ELFCLASS32, EM_PPC, ELFDATA2MSB)) return false;
        return HasPackHeader() || HasOfficialPackHeader();
    }

    public override void DoPack(OutputFile output, Options options)
        => DoPackElfFile(ELFCLASS32, EM_PPC, ELFDATA2MSB, output, options);

    public override void DoUnpack(OutputFile output, Options options)
        => DoUnpackElfFile(ELFCLASS32, EM_PPC, ELFDATA2MSB, output, options);
}
