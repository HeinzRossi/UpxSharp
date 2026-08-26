using System;
using System.Runtime.InteropServices;

namespace UpxSharp.Shell.ExplorerCommand;

[ComVisible(true)]
[Guid(ComRegistration.CompressCommandClsid)]
[ProgId(ComRegistration.CompressCommandProgId)]
public sealed class CompressCommand : BaseExplorerCommand
{
    protected override Guid CommandGuid => new(ComRegistration.CompressCommandClsid);
    protected override string CliCommand => "compress";
    protected override string Title => "Comprimir com UpxSharp";
    protected override string ToolTip => "Comprimir executável(is) selecionado(s) com UPX";
}
