using System;
using System.Runtime.InteropServices;

namespace UpxSharp.Shell.Interop;

[ComImport]
[Guid("1C9CD5BB-98E9-4491-A60F-31AACC72B83C")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IObjectWithSelection
{
    void SetSelection(IShellItemArray? psia);
    void GetSelection(in Guid riid, [MarshalAs(UnmanagedType.Interface)] out object? ppv);
}
