using UpxSharp.Core;
using UpxSharp.IO;

namespace UpxSharp.Packers.Windows;

public sealed class PeFile32 : PeFile
{
    public PeFile32(InputFile file, Options options) : base(file, options) { }

    public override UpxFormat GetFormat() => UpxFormat.Win32Pe;
    public override string GetName() => "win32/pe";
    public override byte GetVersion() => 7;
    public override bool IsBigEndian => false;

    public override bool? CanPack()
        => IsPeFile(IMAGE_FILE_MACHINE_I386)
            ? IsAlreadyPacked() ? null : true
            : false;

    public override bool? CanUnpack()
    {
        if (!IsPeFile(IMAGE_FILE_MACHINE_I386)) return false;
        return HasPackHeader() || HasOfficialPackHeader();
    }

    public override void DoPack(OutputFile output, Options options)
        => DoPackPeFile(IMAGE_FILE_MACHINE_I386, output, options);

    public override void DoUnpack(OutputFile output, Options options)
        => DoUnpackPeFile(IMAGE_FILE_MACHINE_I386, output, options);
}
