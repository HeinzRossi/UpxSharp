using System;
using System.Runtime.InteropServices;

namespace UpxSharp.Shell.Interop;

[ComImport]
[Guid("B63EA76D-1F85-456F-A19C-48159EFA858B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellItemArray
{
    void BindToHandler(nint pbc, in Guid bhid, in Guid riid, out nint ppvOut);
    void GetPropertyStore(int flags, in Guid riid, out nint ppv);
    void GetPropertyDescriptionList(nint keyType, in Guid riid, out nint ppv);
    void GetAttributes(uint attribFlags, uint sfgaoMask, out uint psfgaoAttribs);
    void GetCount(out uint pdwNumItems);
    void GetItemAt(uint dwIndex, out IShellItem ppsi);
    void EnumItems(out nint ppenumShellItems);
}
