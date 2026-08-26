using UpxSharp.Core;

namespace UpxSharp.Linker;

public abstract class ElfLinker
{
    private readonly Dictionary<string, uint> _symbols = [];
    protected byte[] _stubData = [];

    public void Init(byte[] stubBytes)
    {
        _stubData = stubBytes.ToArray();
        ParseStub();
    }

    public void DefineSymbol(string name, uint value)
        => _symbols[name] = value;

    public uint GetSymbol(string name)
        => _symbols.TryGetValue(name, out uint value)
            ? value
            : throw new InternalErrorException($"Símbolo não definido: {name}");

    public byte[] Relocate()
    {
        byte[] relocated = _stubData.ToArray();
        ApplyRelocations(relocated);
        return relocated;
    }

    protected virtual void ParseStub() { }

    protected abstract void ApplyRelocations(Span<byte> stub);

    public abstract (int Offset, int Size) GetLoaderSection(string name);
}
