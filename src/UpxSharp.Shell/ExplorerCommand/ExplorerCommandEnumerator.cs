using UpxSharp.Shell.Interop;

namespace UpxSharp.Shell.ExplorerCommand;

internal sealed class ExplorerCommandEnumerator(IReadOnlyList<IExplorerCommand> commands) : IEnumExplorerCommand
{
    private int _index;

    public void Next(uint celt, out IExplorerCommand? pUICommand, out uint pceltFetched)
    {
        if (celt == 0 || _index >= commands.Count)
        {
            pUICommand = null;
            pceltFetched = 0;
            return;
        }

        pUICommand = commands[_index++];
        pceltFetched = 1;
    }

    public void Skip(uint celt) => _index = Math.Min(commands.Count, _index + checked((int)celt));

    public void Reset() => _index = 0;

    public void Clone(out IEnumExplorerCommand ppenum)
    {
        var clone = new ExplorerCommandEnumerator(commands);
        clone._index = _index;
        ppenum = clone;
    }
}
