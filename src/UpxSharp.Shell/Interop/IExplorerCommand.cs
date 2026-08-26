using System;
using System.Runtime.InteropServices;

namespace UpxSharp.Shell.Interop;

[ComImport]
[Guid("A08CE4D0-FA25-44AB-B57C-C7B1C323E0B9")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IExplorerCommand
{
    void GetTitle(IShellItemArray? psiItemArray, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
    void GetIcon(IShellItemArray? psiItemArray, [MarshalAs(UnmanagedType.LPWStr)] out string ppszIcon);
    void GetToolTip(IShellItemArray? psiItemArray, [MarshalAs(UnmanagedType.LPWStr)] out string ppszInfotip);
    void GetCanonicalName(out Guid pguidCommandName);
    void GetState(IShellItemArray? psiItemArray, bool fOkToBeSlow, out ExplorerCommandState pCmdState);
    void Invoke(IShellItemArray? psiItemArray, nint pbc);
    void GetFlags(out ExplorerCommandFlags pFlags);
    void EnumSubCommands(out IEnumExplorerCommand? ppEnum);
}
