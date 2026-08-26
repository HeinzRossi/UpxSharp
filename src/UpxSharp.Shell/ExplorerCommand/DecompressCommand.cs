using System;
using System.Runtime.InteropServices;

namespace UpxSharp.Shell.ExplorerCommand;

[ComVisible(true)]
[Guid(ComRegistration.DecompressCommandClsid)]
[ProgId(ComRegistration.DecompressCommandProgId)]
public sealed class DecompressCommand : BaseExplorerCommand
{
    protected override Guid CommandGuid => new(ComRegistration.DecompressCommandClsid);
    protected override string CliCommand => "decompress";
    protected override string Title => "Descomprimir com UpxSharp";
    protected override string ToolTip => "Descomprimir executável(is) selecionado(s) com UPX";
}
