using System.Buffers.Binary;

namespace UpxSharp.Filters;

public sealed class FilterArmThumb : IFilter
{
    private const ushort BranchMask = 0xF800;
    private const ushort BranchValue = 0xE000;
    private const ushort ImmediateMask = 0x07FF;

    public byte Id { get; }

    public FilterArmThumb(byte id) => Id = id;

    public void Filter(FilterState state)
        => Transform(state, encode: true);

    public void Unfilter(FilterState state)
        => Transform(state, encode: false);

    private static void Transform(FilterState state, bool encode)
    {
        var buf = state.Buffer.AsSpan(0, state.BufLen);
        uint calls = 0;

        for (int i = 0; i <= buf.Length - 2; i += 2)
        {
            ushort instruction = BinaryPrimitives.ReadUInt16LittleEndian(buf[i..]);
            if ((instruction & BranchMask) != BranchValue)
                continue;

            ushort value = (ushort)(instruction & ImmediateMask);
            ushort delta = (ushort)((((uint)i + 4 + state.AddValue) >> 1) & ImmediateMask);
            value = encode
                ? (ushort)((value + delta) & ImmediateMask)
                : (ushort)((value - delta) & ImmediateMask);

            instruction = (ushort)((instruction & ~ImmediateMask) | value);
            BinaryPrimitives.WriteUInt16LittleEndian(buf[i..], instruction);
            calls++;
        }

        if (encode)
            state.Calls = calls;
    }
}
