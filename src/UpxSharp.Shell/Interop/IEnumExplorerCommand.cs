using System;
using System.Runtime.InteropServices;

namespace UpxSharp.Shell.Interop;

[ComImport]
[Guid("A88826F8-186F-4987-AADE-EA0CEF8FBFE8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IEnumExplorerCommand
{
    void Next(uint celt, [MarshalAs(UnmanagedType.Interface)] out IExplorerCommand? pUICommand, out uint pceltFetched);
    void Skip(uint celt);
    void Reset();
    void Clone(out IEnumExplorerCommand ppenum);
}
