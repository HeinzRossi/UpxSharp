using UpxSharp.Core;
using UpxSharp.IO;

namespace UpxSharp.Packers.Linux;

public sealed class PackLinuxElf64Ppc : PackLinuxElf
{
    public PackLinuxElf64Ppc(InputFile file, Options options) : base(file, options) { }

    public override UpxFormat GetFormat() => UpxFormat.LinuxElf64Ppc;
    public override string GetName() => "linux/ppc64";
    public override byte GetVersion() => 42;
    public override bool IsBigEndian => true;

    public override bool? CanPack()
        => TryGetDataEncoding() is not null
            ? IsAlreadyPacked() ? null : true
            : false;

    public override bool? CanUnpack()
    {
        if (TryGetDataEncoding() is null) return false;
        return HasPackHeader() || HasOfficialPackHeader();
    }

    public override void DoPack(OutputFile output, Options options)
        => DoPackElfFile(ELFCLASS64, EM_PPC64, GetDataEncoding(), output, options);

    public override void DoUnpack(OutputFile output, Options options)
        => DoUnpackElfFile(ELFCLASS64, EM_PPC64, GetDataEncoding(), output, options);

    private byte GetDataEncoding()
        => TryGetDataEncoding()
           ?? throw new CantPackException("ELF PPC64 endianness incorreto");

    private byte? TryGetDataEncoding()
    {
        if (IsElfFile(ELFCLASS64, EM_PPC64, ELFDATA2MSB)) return ELFDATA2MSB;
        if (IsElfFile(ELFCLASS64, EM_PPC64, ELFDATA2LSB)) return ELFDATA2LSB;
        return null;
    }
}
