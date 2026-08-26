using System;
using System.Runtime.InteropServices;
using UpxSharp.Shell.Interop;

namespace UpxSharp.Shell.ExplorerCommand;

[ComVisible(true)]
[Guid(ComRegistration.ProviderClsid)]
[ProgId(ComRegistration.ProviderProgId)]
public sealed class UpxCommandProvider : IExplorerCommandProvider
{
    public void GetCommands(nint punkSite, in Guid riid, out object? ppv)
        => ppv = new ExplorerCommandEnumerator(CreateCommands());

    public void GetCommand(in Guid rguidCommandId, in Guid riid, out object? ppv)
    {
        ppv = rguidCommandId == new Guid(ComRegistration.CompressCommandClsid)
            ? new CompressCommand()
            : rguidCommandId == new Guid(ComRegistration.DecompressCommandClsid)
                ? new DecompressCommand()
                : rguidCommandId == new Guid(ComRegistration.TestCommandClsid)
                    ? new TestCommand()
                    : null;
    }

    internal static IReadOnlyList<IExplorerCommand> CreateCommands()
        =>
        [
            new CompressCommand(),
            new DecompressCommand(),
            new TestCommand(),
        ];
}
