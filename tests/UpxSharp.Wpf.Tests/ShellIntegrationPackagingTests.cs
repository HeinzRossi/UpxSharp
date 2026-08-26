using System.Xml.Linq;
using Xunit;

namespace UpxSharp.Wpf.Tests;

public sealed class ShellIntegrationPackagingTests
{
    private const string ProviderClsid = "7F5D0D52-7266-4B01-96D0-744153A0BB02";

    [Fact]
    public void WpfOutput_ContainsShellIntegrationArtifacts()
    {
        string outputDirectory = GetWpfOutputDirectory();

        Assert.True(File.Exists(Path.Combine(outputDirectory, "UpxSharp.Shell.comhost.dll")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "UpxSharp.Shell.clsidmap")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "UpxSharp.Shell.dll")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "UpxSharp.Cli.exe")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "UpxSharp.Sparse", "AppxManifest.xml")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "UpxSharp.Sparse", "Assets", "Square44x44Logo.png")));
    }

    [Fact]
    public void SparseManifest_RegistersProviderForExeOnly()
    {
        string manifestPath = Path.Combine(GetWpfOutputDirectory(), "UpxSharp.Sparse", "AppxManifest.xml");
        XDocument document = XDocument.Load(manifestPath);

        XNamespace com = "http://schemas.microsoft.com/appx/manifest/com/windows10";
        XNamespace desktop5 = "http://schemas.microsoft.com/appx/manifest/desktop/windows10/5";

        XElement comClass = document.Descendants(com + "Class").Single();
        Assert.Equal(ProviderClsid, comClass.Attribute("Id")?.Value, ignoreCase: true);
        Assert.Equal("UpxSharp.Shell.comhost.dll", comClass.Attribute("Path")?.Value);

        XElement[] itemTypes = document.Descendants(desktop5 + "ItemType").ToArray();
        XElement itemType = Assert.Single(itemTypes);
        Assert.Equal("*", itemType.Attribute("Type")?.Value);

        XElement verb = itemType.Descendants(desktop5 + "Verb").Single();
        Assert.Equal(ProviderClsid, verb.Attribute("Clsid")?.Value, ignoreCase: true);
    }

    private static string GetWpfOutputDirectory()
    {
        string directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            string candidate = Path.Combine(
                directory,
                "src",
                "UpxSharp.Wpf",
                "bin",
                "Debug",
                "net10.0-windows");

            if (Directory.Exists(candidate))
                return candidate;

            directory = Directory.GetParent(directory)?.FullName ?? string.Empty;
        }

        throw new DirectoryNotFoundException("Output do projeto UpxSharp.Wpf não encontrado.");
    }
}
