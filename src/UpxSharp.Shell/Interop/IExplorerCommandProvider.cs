using System;
using System.Runtime.InteropServices;

namespace UpxSharp.Shell.Interop;

[ComImport]
[Guid("64961751-0835-43C0-8FFE-D57686530E64")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IExplorerCommandProvider
{
    void GetCommands(nint punkSite, in Guid riid, [MarshalAs(UnmanagedType.Interface)] out object? ppv);
    void GetCommand(in Guid rguidCommandId, in Guid riid, [MarshalAs(UnmanagedType.Interface)] out object? ppv);
}
