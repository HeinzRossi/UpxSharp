using UpxSharp.Core;
using UpxSharp.IO;
using UpxSharp.Packers.Legacy;
using UpxSharp.Packers.Linux;
using UpxSharp.Packers.MacOs;
using UpxSharp.Packers.Windows;

namespace UpxSharp.Packers;

public static class PackMaster
{
    private static readonly List<Func<InputFile, Options, PackerBase>> PackerFactories =
        new()
        {
            (f, o) => new PeFile32(f, o),
            (f, o) => new PeFile64(f, o),
            (f, o) => new PackLinuxElf32x86(f, o),
            (f, o) => new PackLinuxElf64Amd(f, o),
            (f, o) => new PackLinuxElf32Arm(f, o),
            (f, o) => new PackLinuxElf64Arm(f, o),
            (f, o) => new PackLinuxElf32Mips(f, o),
            (f, o) => new PackLinuxElf32MipsBe(f, o),
            (f, o) => new PackLinuxElf64Riscv(f, o),
            (f, o) => new PackLinuxElf32Ppc(f, o),
            (f, o) => new PackLinuxElf64Ppc(f, o),
            (f, o) => new PackMachI386(f, o),
            (f, o) => new PackMachAMD64(f, o),
            (f, o) => new PackVmlinuzI386(f, o),
            (f, o) => new PackDosExe(f, o),
            (f, o) => new PackDosCom(f, o),
            (f, o) => new PackAtari(f, o),
            (f, o) => new PackPs1(f, o),
            (f, o) => new DefaultPacker(f, o),
        };

    public static PackerBase FindPacker(InputFile file, Options options)
    {
        foreach (var factory in PackerFactories)
        {
            var packer = factory(file, options);
            bool? result = packer.CanPack();
            if (result == true) return packer;
            if (result == null) throw new CantPackException($"Erro ao analisar '{file.Name}'");
        }

        throw new CantPackException($"Formato não reconhecido ou não suportado: '{file.Name}'");
    }

    public static PackerBase FindUnpacker(InputFile file, Options options)
    {
        foreach (var factory in PackerFactories)
        {
            var packer = factory(file, options);
            bool? result = packer.CanUnpack();
            if (result == true) return packer;
            if (result == null) throw new NotPackedException();
        }

        throw new NotPackedException();
    }
}
