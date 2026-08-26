using System.Buffers.Binary;

namespace UpxSharp.Filters;

public sealed class FilterArm32 : IFilter
{
    private const uint BranchMask = 0x0E000000;
    private const uint BranchValue = 0x0A000000;
    private const uint ImmediateMask = 0x00FFFFFF;

    public byte Id { get; }

    public FilterArm32(byte id) => Id = id;

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
            if ((instruction & BranchMask) != BranchValue)
                continue;

            uint value = instruction & ImmediateMask;
            uint delta = (((uint)i + 8 + state.AddValue) >> 2) & ImmediateMask;
            value = encode
                ? (value + delta) & ImmediateMask
                : (value - delta) & ImmediateMask;

            instruction = (instruction & ~ImmediateMask) | value;
            BinaryPrimitives.WriteUInt32LittleEndian(buf[i..], instruction);
            calls++;
        }

        if (encode)
            state.Calls = calls;
    }
}
