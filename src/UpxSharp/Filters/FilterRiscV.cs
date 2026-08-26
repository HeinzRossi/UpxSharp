using System.Buffers.Binary;

namespace UpxSharp.Filters;

public sealed class FilterRiscV : IFilter
{
    private const uint AuipcOpcode = 0x17;

    public FilterRiscV(byte id) => Id = id;

    public byte Id { get; }

    public void Filter(FilterState state)
        => throw new NotSupportedException("Codificação do filtro RISC-V AUIPC ainda não é usada pelo packer");

    public void Unfilter(FilterState state)
    {
        Span<byte> buffer = state.Buffer.AsSpan(0, state.BufLen);
        int scanLimit = buffer.Length - 8;
        int offset = 0;
        uint calls = 0;

        while (offset <= scanLimit)
        {
            uint first = BinaryPrimitives.ReadUInt32LittleEndian(buffer[offset..]);
            if ((first & 0x7F) != AuipcOpcode)
            {
                offset += GetInstructionLength((byte)first);
                continue;
            }

            uint second = BinaryPrimitives.ReadUInt32LittleEndian(buffer[(offset + 4)..]);
            uint auipcRegister = (second >> 7) & 0x1F;
            if (IsFilteredPair(second, auipcRegister))
                calls++;

            uint address = BinaryPrimitives.ReadUInt32BigEndian(buffer[(offset + 1)..]);
            address = (address & ~0xFFu)
                | ((address & 0x7Fu) << 1)
                | ((first >> 7) & 1u);
            address = unchecked(address - (uint)offset);
            address = unchecked(address + ((address & (1u << 11)) << 1));

            uint restoredFirst = (address & ~0xFFFu) | (auipcRegister << 7) | AuipcOpcode;
            uint restoredSecond = unchecked((address << 20) | (second >> 12));
            BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], restoredFirst);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer[(offset + 4)..], restoredSecond);
            offset += 4;
        }

        state.Calls = calls;
    }

    private static bool IsFilteredPair(uint second, uint register)
    {
        uint opcode = (second >> 12) & 0x7F;
        uint function = (second >> 24) & 0x07;
        uint sourceRegister = (second >> 27) & 0x1F;
        return sourceRegister == register
            && (opcode == 0x03
                || (opcode == 0x67 && function == 0)
                || (opcode == 0x13 && function == 0));
    }

    private static int GetInstructionLength(byte first)
    {
        int length = 2;
        if ((first & 3) == 3)
        {
            length += 2;
            if (((first >> 2) & 7) == 7)
                length += 2;
        }
        return length;
    }
}
