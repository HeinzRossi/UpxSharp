using UpxSharp.Core;

namespace UpxSharp.Filters;

public static class FilterFactory
{
    public static IFilter? Create(byte filterId) => filterId switch
    {
        0x00 => null,
        0x24 or 0x25 or 0x26 or 0x36 or 0x46 or 0x49 => new FilterX86(filterId),
        0x52 => new FilterArm64(filterId),
        0x55 => new FilterRiscV(filterId),
        >= 0x01 and <= 0x1F => new FilterX86(filterId),
        >= 0x40 and <= 0x4F => new FilterArm32(filterId),
        >= 0x50 and <= 0x5F => new FilterArmThumb(filterId),
        >= 0x80 and <= 0x8F => new FilterMips(filterId),
        >= 0x90 and <= 0x9F => new FilterPpc(filterId),
        _ => throw new CantPackException($"Filtro desconhecido: 0x{filterId:X2}"),
    };

    public static IEnumerable<byte> GetFiltersForArch(UpxFormat format)
    {
        if (format == UpxFormat.LinuxElfi386
            || format == UpxFormat.LinuxElfAmd64
            || format == UpxFormat.MachI386
            || format == UpxFormat.MachAMD64
            || format == UpxFormat.Vmlinuzi386
            || format == UpxFormat.DosCom
            || format == UpxFormat.DosExe
            || format == UpxFormat.Win32Pe
            || format == UpxFormat.Win64Pe)
        {
            yield return 0x00;
            foreach (var i in Enumerable.Range(0x01, 0x1F))
                yield return (byte)i;
            yield break;
        }

        if (format == UpxFormat.LinuxElfArmLE || format == UpxFormat.LinuxElfArm64LE)
        {
            yield return 0x00;
            foreach (var i in Enumerable.Range(0x40, 0x10))
                yield return (byte)i;
            foreach (var i in Enumerable.Range(0x50, 0x10)
                         .Where(i => i is not (0x52 or 0x55)))
                yield return (byte)i;
            yield break;
        }

        if (format == UpxFormat.LinuxElfMipsel || format == UpxFormat.LinuxElfMips)
        {
            yield return 0x00;
            foreach (var i in Enumerable.Range(0x80, 0x10))
                yield return (byte)i;
            yield break;
        }

        if (format == UpxFormat.LinuxElf32Ppc || format == UpxFormat.LinuxElf64Ppc)
        {
            yield return 0x00;
            foreach (var i in Enumerable.Range(0x90, 0x10))
                yield return (byte)i;
            yield break;
        }

        yield return 0x00;
    }
}
