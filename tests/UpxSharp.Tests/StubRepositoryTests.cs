using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using UpxSharp.Core;
using UpxSharp.Stubs;
using Xunit;

namespace UpxSharp.Tests;

public class StubRepositoryTests
{
    public static IEnumerable<object[]> ImplementedStubFormats()
    {
        yield return [UpxFormat.LinuxElfi386];
        yield return [UpxFormat.LinuxElfAmd64];
        yield return [UpxFormat.LinuxElfArmLE];
        yield return [UpxFormat.LinuxElfArm64LE];
        yield return [UpxFormat.LinuxElfMipsel];
        yield return [UpxFormat.LinuxElfMips];
        yield return [UpxFormat.LinuxElf32Ppc];
        yield return [UpxFormat.LinuxElf64Ppc];
        yield return [UpxFormat.LinuxElfRiscv64];
        yield return [UpxFormat.MachI386];
        yield return [UpxFormat.MachAMD64];
        yield return [UpxFormat.Vmlinuzi386];
        yield return [UpxFormat.DosCom];
        yield return [UpxFormat.DosExe];
        yield return [UpxFormat.AtariTos];
        yield return [UpxFormat.Ps1Exe];
        yield return [UpxFormat.Win32Pe];
        yield return [UpxFormat.Win64Pe];
    }

    public static IEnumerable<object[]> UnsupportedStubFormats()
    {
        yield return [UpxFormat.DosSys];
        yield return [UpxFormat.Djgpp2Coff];
    }

    [Theory]
    [MemberData(nameof(ImplementedStubFormats))]
    public void Load_WhenFormatMapped_ReturnsEmbeddedStub(UpxFormat format)
    {
        byte[] stub = StubRepository.Load(format);

        Assert.NotEmpty(stub);
    }

    [Theory]
    [MemberData(nameof(ImplementedStubFormats))]
    public void GetMetadata_WhenFormatMapped_ReturnsMetadataWithMatchingHash(UpxFormat format)
    {
        StubMetadata metadata = StubRepository.GetMetadata(format);
        byte[] stub = StubRepository.Load(format);

        Assert.True(metadata.IsFake || metadata.IsReal);
        Assert.Equal(stub.Length, metadata.Size);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(stub)), metadata.Sha256);
    }

    [Theory]
    [MemberData(nameof(ImplementedStubFormats))]
    public void Load_WhenRequireReal_RejectsOnlyFakeStubs(UpxFormat format)
    {
        StubMetadata metadata = StubRepository.GetMetadata(format);

        if (metadata.IsFake)
        {
            Assert.Throws<InternalErrorException>(() => StubRepository.Load(format, requireReal: true));
        }
        else
        {
            Assert.NotEmpty(StubRepository.Load(format, requireReal: true));
        }
    }

    [Theory]
    [MemberData(nameof(UnsupportedStubFormats))]
    public void Load_WhenFormatUnsupported_ThrowsNotImplementedException(UpxFormat format)
        => Assert.Throws<NotImplementedException>(() => StubRepository.Load(format));

    [Fact]
    public void LoadResource_WhenResourceMissing_ThrowsInternalErrorException()
        => Assert.Throws<InternalErrorException>(() =>
            StubRepository.LoadResource("UpxSharp.Stubs.Resources.missing.stub"));

    [Fact]
    public void ImportRealStubs_WhenAllFilesExist_CopiesFilesAndWritesRealManifest()
    {
        using var temp = new TempDirectory();
        string source = Path.Combine(temp.Path, "source");
        string destination = Path.Combine(temp.Path, "destination");
        string manifest = Path.Combine(temp.Path, "stubs.manifest.json");
        Directory.CreateDirectory(source);

        foreach (StubMetadata metadata in StubRepository.GetAllMetadata())
            File.WriteAllText(Path.Combine(source, metadata.ResourceName), $"real-{metadata.ResourceName}");

        ProcessResult result = RunImportScript(source, destination, manifest);

        Assert.Equal(0, result.ExitCode);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifest));
        JsonElement stubs = document.RootElement.GetProperty("stubs");
        Assert.Equal(StubRepository.GetAllMetadata().Count, stubs.GetArrayLength());

        foreach (JsonElement entry in stubs.EnumerateArray())
        {
            string resourceName = entry.GetProperty("resourceName").GetString()!;
            string copied = Path.Combine(destination, resourceName);

            Assert.True(File.Exists(copied));
            Assert.Equal("real", entry.GetProperty("kind").GetString());
            Assert.Equal(new FileInfo(copied).Length, entry.GetProperty("size").GetInt32());
            Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(copied))), entry.GetProperty("sha256").GetString());
        }
    }

    [Fact]
    public void ImportRealStubs_WhenFileMissing_FailsWithClearError()
    {
        using var temp = new TempDirectory();
        string source = Path.Combine(temp.Path, "source");
        Directory.CreateDirectory(source);

        ProcessResult result = RunImportScript(source, Path.Combine(temp.Path, "destination"), Path.Combine(temp.Path, "manifest.json"));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Missing required real stub", result.Error + result.Output);
    }

    private static ProcessResult RunImportScript(string source, string destination, string manifest)
    {
        string script = Path.Combine(FindRepoRoot(), "tools", "import-real-stubs.ps1");
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(script);
        startInfo.ArgumentList.Add("-SourceDir");
        startInfo.ArgumentList.Add(source);
        startInfo.ArgumentList.Add("-DestinationDir");
        startInfo.ArgumentList.Add(destination);
        startInfo.ArgumentList.Add("-ManifestPath");
        startInfo.ArgumentList.Add(manifest);

        using Process process = Process.Start(startInfo)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, output, error);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "UpxSharp.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Repo root not found");
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "upxsharp-stubs-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
