namespace UpxSharp.Filters;

public sealed class FilterState
{
    public byte Id { get; set; }
    public byte[] Buffer { get; set; } = [];
    public int BufLen { get; set; }
    public uint AddValue { get; set; }
    public uint Calls { get; set; }
    public byte Cto { get; set; }
    public byte NMru { get; set; }
}
