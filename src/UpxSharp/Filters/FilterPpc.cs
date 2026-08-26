using System.Buffers.Binary;

namespace UpxSharp.Filters;

public sealed class FilterPpc : IFilter
{
    private const uint BranchMask = 0xFC000000;
    private const uint BranchValue = 0x48000000;
    private const uint LiMask = 0x03FFFFFC;

    public byte Id { get; }

    public FilterPpc(byte id) => Id = id;

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
            uint instruction = BinaryPrimitives.ReadUInt32BigEndian(buf[i..]);
            if ((instruction & BranchMask) != BranchValue)
                continue;

            uint value = (instruction & LiMask) >> 2;
            uint delta = (((uint)i + 4 + state.AddValue) >> 2) & 0x00FFFFFF;
            value = encode
                ? (value + delta) & 0x00FFFFFF
                : (value - delta) & 0x00FFFFFF;

            instruction = (instruction & ~LiMask) | (value << 2);
            BinaryPrimitives.WriteUInt32BigEndian(buf[i..], instruction);
            calls++;
        }

        if (encode)
            state.Calls = calls;
    }
}
