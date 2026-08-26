using System;
using System.Runtime.InteropServices;

namespace UpxSharp.Shell.ExplorerCommand;

[ComVisible(true)]
[Guid(ComRegistration.TestCommandClsid)]
[ProgId(ComRegistration.TestCommandProgId)]
public sealed class TestCommand : BaseExplorerCommand
{
    protected override Guid CommandGuid => new(ComRegistration.TestCommandClsid);
    protected override string CliCommand => "test";
    protected override string Title => "Testar com UpxSharp";
    protected override string ToolTip => "Testar executável(is) selecionado(s) com UPX";
}
