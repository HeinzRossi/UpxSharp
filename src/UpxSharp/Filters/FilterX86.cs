using System.Buffers.Binary;

namespace UpxSharp.Filters;

public sealed class FilterX86 : IFilter
{
    public byte Id { get; }

    public FilterX86(byte id) => Id = id;

    public void Filter(FilterState state)
    {
        var buf = state.Buffer.AsSpan(0, state.BufLen);
        uint calls = 0;

        for (int i = 0; i < buf.Length - 4; i++)
        {
            if (buf[i] is not (0xE8 or 0xE9)) continue;
            uint rel = BinaryPrimitives.ReadUInt32LittleEndian(buf[(i + 1)..]);
            uint abs = rel + (uint)(i + 5) + state.AddValue;
            BinaryPrimitives.WriteUInt32LittleEndian(buf[(i + 1)..], abs);
            calls++;
            i += 4;
        }

        state.Calls = calls;
    }

    public void Unfilter(FilterState state)
    {
        if (Id is 0x24 or 0x25 or 0x26 or 0x36 or 0x46 or 0x49)
        {
            UnfilterOfficialCto(state);
            return;
        }

        var buf = state.Buffer.AsSpan(0, state.BufLen);

        for (int i = 0; i < buf.Length - 4; i++)
        {
            if (buf[i] is not (0xE8 or 0xE9)) continue;
            uint abs = BinaryPrimitives.ReadUInt32LittleEndian(buf[(i + 1)..]);
            uint rel = abs - (uint)(i + 5) - state.AddValue;
            BinaryPrimitives.WriteUInt32LittleEndian(buf[(i + 1)..], rel);
            i += 4;
        }
    }

    private void UnfilterOfficialCto(FilterState state)
    {
        var buf = state.Buffer.AsSpan(0, state.BufLen);
        if (buf.Length < 6)
            return;

        uint cto = (uint)state.Cto << 24;
        int lastCall = 0;
        uint calls = 0;

        for (int i = 0; i < buf.Length - 5; i++)
        {
            bool isCallOrJump = Id switch
            {
                0x24 => buf[i] == 0xE8,
                0x25 => buf[i] == 0xE9,
                _ => buf[i] is 0xE8 or 0xE9,
            };
            bool isConditionalJump = Id == 0x49
                && lastCall != i
                && i > 0
                && buf[i - 1] == 0x0F
                && buf[i] is >= 0x80 and <= 0x8F;
            if (!isCallOrJump && !isConditionalJump)
                continue;

            uint encoded = BinaryPrimitives.ReadUInt32BigEndian(buf[(i + 1)..]);
            if (buf[i + 1] != state.Cto)
                continue;

            uint relative = unchecked(encoded - (uint)i - 1u - state.AddValue - cto);
            BinaryPrimitives.WriteUInt32LittleEndian(buf[(i + 1)..], relative);
            calls++;
            i += 4;
            lastCall = i + 1;
        }

        state.Calls = calls;
    }
}
