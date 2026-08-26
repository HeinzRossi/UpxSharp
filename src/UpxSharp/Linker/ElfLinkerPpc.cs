using System.Buffers.Binary;

namespace UpxSharp.Linker;

public sealed class ElfLinkerPpc : ElfLinkerScaffold
{
    protected override void WritePatch32(Span<byte> stub, uint value)
        => BinaryPrimitives.WriteUInt32BigEndian(stub, value);
}
