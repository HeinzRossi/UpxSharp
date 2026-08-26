using UpxSharp.Shell;
using UpxSharp.Shell.ExplorerCommand;
using UpxSharp.Shell.Interop;
using Xunit;
using System.Runtime.InteropServices;

namespace UpxSharp.Shell.Tests;

public sealed class ExplorerCommandTests
{
    [Fact]
    public void Provider_CreateCommands_ReturnsExpectedCommands()
    {
        IReadOnlyList<IExplorerCommand> commands = UpxCommandProvider.CreateCommands();

        Assert.Collection(
            commands,
            command => Assert.IsType<CompressCommand>(command),
            command => Assert.IsType<DecompressCommand>(command),
            command => Assert.IsType<TestCommand>(command));
    }

    [Theory]
    [MemberData(nameof(CommandMetadata))]
    public void Commands_ReturnExpectedMetadata(IExplorerCommand command, string title, string guid)
    {
        command.GetTitle(null, out string actualTitle);
        command.GetCanonicalName(out Guid actualGuid);
        command.GetFlags(out ExplorerCommandFlags flags);
        command.GetToolTip(null, out string tooltip);

        Assert.Equal(title, actualTitle);
        Assert.Equal(new Guid(guid), actualGuid);
        Assert.Equal(ExplorerCommandFlags.ECF_DEFAULT, flags);
        Assert.Contains("UPX", tooltip);
    }

    [Fact]
    public void Provider_GetCommand_ReturnsCommandByGuid()
    {
        var provider = new UpxCommandProvider();
        Guid commandId = new(ComRegistration.DecompressCommandClsid);
        Guid riid = typeof(IExplorerCommand).GUID;

        provider.GetCommand(in commandId, in riid, out object? command);

        Assert.IsType<DecompressCommand>(command);
    }

    [Fact]
    public void ComVisibleClasses_UseExpectedClsids()
    {
        AssertComClass<UpxCommandProvider>(ComRegistration.ProviderClsid);
        AssertComClass<CompressCommand>(ComRegistration.CompressCommandClsid);
        AssertComClass<DecompressCommand>(ComRegistration.DecompressCommandClsid);
        AssertComClass<TestCommand>(ComRegistration.TestCommandClsid);
    }

    private static void AssertComClass<T>(string expectedClsid)
    {
        Type type = typeof(T);
        var guid = type.GetCustomAttributes(typeof(GuidAttribute), inherit: false)
            .OfType<GuidAttribute>()
            .Single();
        var comVisible = type.GetCustomAttributes(typeof(ComVisibleAttribute), inherit: false)
            .OfType<ComVisibleAttribute>()
            .Single();

        Assert.True(comVisible.Value);
        Assert.Equal(expectedClsid, guid.Value, ignoreCase: true);
    }

    public static TheoryData<IExplorerCommand, string, string> CommandMetadata()
    {
        return new TheoryData<IExplorerCommand, string, string>
        {
            { new CompressCommand(), "Comprimir com UpxSharp", ComRegistration.CompressCommandClsid },
            { new DecompressCommand(), "Descomprimir com UpxSharp", ComRegistration.DecompressCommandClsid },
            { new TestCommand(), "Testar com UpxSharp", ComRegistration.TestCommandClsid },
        };
    }
}
