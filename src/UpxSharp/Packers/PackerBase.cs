using UpxSharp.Core;
using UpxSharp.IO;
using UpxSharp.Official;

namespace UpxSharp.Packers;

public abstract class PackerBase
{
    protected readonly InputFile File;

    protected PackerBase(InputFile file) => File = file;

    public abstract UpxFormat GetFormat();
    public abstract string GetName();
    public abstract byte GetVersion();
    public abstract bool IsBigEndian { get; }

    public abstract bool? CanPack();
    public abstract bool? CanUnpack();

    public abstract void DoPack(OutputFile output, Options options);
    public abstract void DoUnpack(OutputFile output, Options options);

    protected byte[] ReadAllBytesForAnalysis(string context)
    {
        if (File.Length > int.MaxValue)
            throw new CantPackException($"Arquivo {context} grande demais para anÃ¡lise");

        byte[] buffer = new byte[File.Length];
        File.ReadExact(buffer, 0);
        return buffer;
    }

    protected bool HasScaffoldPackHeader()
    {
        if (File.Length < PackHeader.SerializedSize)
            return false;

        byte[] buffer = ReadAllBytesForAnalysis("scaffold");
        if (PackHeader.TryFindValidTrailer(buffer, out _, out _))
            return true;

        return PackHeader.TryFind(buffer, out int offset, out var header)
            && header is not null
            && header.UFileSize == offset
            && offset + PackHeader.SerializedSize <= buffer.Length;
    }

    protected bool HasOfficialUpx()
    {
        byte[] buffer = ReadAllBytesForAnalysis("UPX oficial");
        return OfficialUpxDetector.TryDetect(buffer, GetFormat(), out _);
    }

    protected bool TryUnpackOfficialUpxIfDetected(ReadOnlySpan<byte> data, OutputFile output)
    {
        if (OfficialUpxDetector.TryDetect(data, GetFormat(), out var metadata) && metadata is not null)
        {
            byte[] unpacked = OfficialUpxUnpacker.Unpack(data, GetFormat());
            output.Write(unpacked);
            return true;
        }

        return false;
    }

    protected void ThrowCorruptedScaffoldIfHeaderPresent(ReadOnlySpan<byte> data, string message)
    {
        if (PackHeader.TryFind(data, out int offset, out var header)
            && header is not null
            && header.UFileSize == offset
            && offset + PackHeader.SerializedSize <= data.Length)
        {
            throw new CorruptedPackException(message);
        }
    }

    protected void ThrowOfficialUpxUnsupportedIfDetected(ReadOnlySpan<byte> data)
    {
        if (OfficialUpxDetector.TryDetect(data, GetFormat(), out var metadata) && metadata is not null)
            OfficialUpxDetector.ThrowUnsupported(metadata);
    }
}
