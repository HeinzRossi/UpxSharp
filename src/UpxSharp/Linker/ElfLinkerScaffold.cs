using System.Buffers.Binary;
using UpxSharp.Core;

namespace UpxSharp.Linker;

public abstract class ElfLinkerScaffold : ElfLinker
{
    private const string LoaderSectionName = ".loader";
    private (int Offset, int Size)? _loaderSection;

    protected override void ParseStub()
    {
        if (_stubData.Length < 12 ||
            _stubData[0] != (byte)'U' ||
            _stubData[1] != (byte)'P' ||
            _stubData[2] != (byte)'X' ||
            _stubData[3] != (byte)'L')
        {
            _loaderSection = (0, _stubData.Length);
            return;
        }

        int offset = BinaryPrimitives.ReadInt32LittleEndian(_stubData.AsSpan(4));
        int size = BinaryPrimitives.ReadInt32LittleEndian(_stubData.AsSpan(8));

        if (offset < 0 || size < 0 || offset + size > _stubData.Length)
            throw new InternalErrorException($"Seção {LoaderSectionName} inválida no stub");

        _loaderSection = (offset, size);
    }

    protected override void ApplyRelocations(Span<byte> stub)
    {
        if (stub.Length < 4)
            throw new InternalErrorException("Stub pequeno demais para relocação");

        WritePatch32(stub, GetSymbol("_patch32"));
    }

    public override (int Offset, int Size) GetLoaderSection(string name)
    {
        if (name != LoaderSectionName || _loaderSection is null)
            throw new InternalErrorException($"Seção não encontrada: {name}");

        return _loaderSection.Value;
    }

    protected abstract void WritePatch32(Span<byte> stub, uint value);
}
