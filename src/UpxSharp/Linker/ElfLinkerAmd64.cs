using System.Buffers.Binary;

namespace UpxSharp.Linker;

public sealed class ElfLinkerAmd64 : ElfLinkerScaffold
{
    protected override void WritePatch32(Span<byte> stub, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(stub, value);
}
