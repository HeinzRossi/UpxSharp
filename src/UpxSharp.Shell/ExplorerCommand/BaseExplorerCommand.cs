using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using UpxSharp.Shell.Interop;

namespace UpxSharp.Shell.ExplorerCommand;

public abstract class BaseExplorerCommand : IExplorerCommand, IObjectWithSelection
{
    private IShellItemArray? _selection;

    protected abstract Guid CommandGuid { get; }
    protected abstract string CliCommand { get; }
    protected abstract string Title { get; }
    protected abstract string ToolTip { get; }

    public void SetSelection(IShellItemArray? psia) => _selection = psia;

    public void GetSelection(in Guid riid, out object? ppv) => ppv = _selection;

    public void GetTitle(IShellItemArray? psiItemArray, out string ppszName) => ppszName = Title;

    public void GetIcon(IShellItemArray? psiItemArray, out string ppszIcon)
    {
        string appPath = Path.Combine(AppContext.BaseDirectory, "UpxSharp.Wpf.exe");
        ppszIcon = $"{appPath},0";
    }

    public void GetToolTip(IShellItemArray? psiItemArray, out string ppszInfotip) => ppszInfotip = ToolTip;

    public void GetCanonicalName(out Guid pguidCommandName) => pguidCommandName = CommandGuid;

    public void GetState(IShellItemArray? psiItemArray, bool fOkToBeSlow, out ExplorerCommandState pCmdState)
    {
        IShellItemArray? selection = psiItemArray ?? _selection;
        pCmdState = ShellSelectionHelper.HasSupportedSelection(selection, fOkToBeSlow)
            ? ExplorerCommandState.ECS_ENABLED
            : ExplorerCommandState.ECS_HIDDEN;
    }

    public void Invoke(IShellItemArray? psiItemArray, nint pbc)
    {
        try
        {
            IReadOnlyList<string> paths = ShellSelectionHelper.GetPaths(psiItemArray ?? _selection);
            if (paths.Count == 0)
                return;

            UpxCliBridge.RunAsync(CliCommand, paths).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException or COMException)
        {
            Debug.WriteLine($"UpxSharp shell command failed: {ex}");
        }
    }

    public void GetFlags(out ExplorerCommandFlags pFlags) => pFlags = ExplorerCommandFlags.ECF_DEFAULT;

    public void EnumSubCommands(out IEnumExplorerCommand? ppEnum) => ppEnum = null;
}
