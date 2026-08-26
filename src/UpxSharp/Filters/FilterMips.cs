using System.Buffers.Binary;

namespace UpxSharp.Filters;

public sealed class FilterMips : IFilter
{
    private const uint OpcodeMask = 0xFC000000;
    private const uint JumpOpcode = 0x08000000;
    private const uint JalOpcode = 0x0C000000;
    private const uint TargetMask = 0x03FFFFFF;

    public byte Id { get; }

    public FilterMips(byte id) => Id = id;

    public void Filter(FilterState state)
        => Transform(state, encode: true);

    public void Unfilter(FilterState state)
        => Transform(state, encode: false);

    private static void Transform(FilterState state, bool encode)
    {
        var buf = state.Buffer.AsSpan(0, state.BufLen);
        uint calls = 0;

        for (int i = 0; i <= buf.Length - 4; i += 4)
        {
            uint instruction = BinaryPrimitives.ReadUInt32LittleEndian(buf[i..]);
            uint opcode = instruction & OpcodeMask;
            if (opcode is not (JumpOpcode or JalOpcode))
                continue;

            uint value = instruction & TargetMask;
            uint delta = (((uint)i + 4 + state.AddValue) >> 2) & TargetMask;
            value = encode
                ? (value + delta) & TargetMask
                : (value - delta) & TargetMask;

            instruction = (instruction & ~TargetMask) | value;
            BinaryPrimitives.WriteUInt32LittleEndian(buf[i..], instruction);
            calls++;
        }

        if (encode)
            state.Calls = calls;
    }
}
