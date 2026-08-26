using System.Buffers.Binary;

namespace UpxSharp.Filters;

public sealed class FilterArm64 : IFilter
{
    private const uint ImmediateMask = 0x03FFFFFF;

    public FilterArm64(byte id) => Id = id;

    public byte Id { get; }

    public void Filter(FilterState state)
        => Transform(state, encode: true);

    public void Unfilter(FilterState state)
        => Transform(state, encode: false);

    private static void Transform(FilterState state, bool encode)
    {
        Span<byte> buffer = state.Buffer.AsSpan(0, state.BufLen);
        uint calls = 0;
        for (int offset = 0; offset < buffer.Length - 4; offset += 4)
        {
            uint instruction = BinaryPrimitives.ReadUInt32LittleEndian(buffer[offset..]);
            if (((instruction >> 26) & 0x1F) != 0x05)
                continue;

            uint immediate = instruction & ImmediateMask;
            uint delta = checked((uint)(offset / 4) + state.AddValue);
            immediate = encode
                ? (immediate + delta) & ImmediateMask
                : (immediate - delta) & ImmediateMask;
            instruction = (instruction & ~ImmediateMask) | immediate;
            BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], instruction);
            calls++;
        }

        state.Calls = calls;
    }
}
