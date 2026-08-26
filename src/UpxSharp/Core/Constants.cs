namespace UpxSharp.Core;

public static class UpxConstants
{
    public const uint MagicLE32 = 0x21585055;  // "UPX!" em LE
    public const uint Magic2LE32 = 0x213d4c53;  // "SL=!" em LE
    public static readonly byte[] MagicBytes = "UPX!"u8.ToArray();

    public const long RsizeMax = 768L * 1024 * 1024;  // 768 MiB
    public const byte VersionMajor = 4;
    public const byte VersionMinor = 2;
}

public enum UpxFormat : byte
{
    DosCom = 1,
    DosSys = 2,
    DosExe = 3,
    Djgpp2Coff = 4,
    Win32Pe = 7,
    LinuxElfi386 = 11,
    Win64Pe = 13,
    LinuxElfAmd64 = 24,
    LinuxElfArmLE = 30,
    LinuxElfArm64LE = 38,
    LinuxElfMipsel = 40,
    LinuxElf32Ppc = 41,
    LinuxElf64Ppc = 42,
    LinuxElfMips = 43,
    LinuxElfRiscv64 = 49,
    MachI386 = 51,
    MachAMD64 = 52,
    Vmlinuzi386 = 60,
    AtariTos = 80,
    Ps1Exe = 90,
}

public enum CompressionMethod : byte
{
    Nrv2bLE32 = 2,
    Nrv2b8 = 3,
    Nrv2bLE16 = 4,
    Nrv2dLE32 = 5,
    Nrv2d8 = 6,
    Nrv2dLE16 = 7,
    Nrv2eLE32 = 8,
    Nrv2e8 = 9,
    Nrv2eLE16 = 10,
    Lzma = 14,
    Deflate = 15,
    Zstd = 18,
}

public enum UpxCommand
{
    Compress,
    Decompress,
    Test,
    List,
    Info,
}
