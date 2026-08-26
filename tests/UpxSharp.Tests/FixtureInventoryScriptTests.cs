using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using UpxSharp.Core;
using UpxSharp.IO;
using UpxSharp.Packers;
using Xunit;

namespace UpxSharp.Tests;

public class FixtureInventoryScriptTests
{
    [Fact]
    public void ImportExternalFixtures_WhenPackedAlreadyExists_CopiesOriginalAndBuildsInventory()
    {
        using var temp = new TempDirectory();
        string sourceDir = Path.Combine(temp.Path, "source");
        string artifactsRoot = Path.Combine(temp.Path, "artifacts");
        string packedDir = Path.Combine(artifactsRoot, "upx-packed-fixtures");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(packedDir);

        byte[] pe32 = CreateSyntheticPe(0x014C);
        File.WriteAllBytes(Path.Combine(sourceDir, "pe32.exe"), pe32);
        File.WriteAllBytes(Path.Combine(packedDir, "Win32Pe-pe32.exe.upx"), Pack(pe32));

        ProcessResult result = RunImportScript(sourceDir, artifactsRoot, dryRun: false, strict: false);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(Path.Combine(artifactsRoot, "real-fixtures", "Win32Pe-pe32.exe")));
        string inventoryPath = Path.Combine(artifactsRoot, "fixture-inventory.json");
        Assert.True(File.Exists(inventoryPath));
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(inventoryPath));
        JsonElement fixture = Assert.Single(document.RootElement.GetProperty("fixtures").EnumerateArray());
        Assert.Equal("Win32Pe", fixture.GetProperty("format").GetString());
        Assert.Equal("Win32Pe-pe32.exe", fixture.GetProperty("path").GetString());
        Assert.Equal("Win32Pe-pe32.exe.upx", fixture.GetProperty("packedPath").GetString());
    }

    [Fact]
    public void ImportExternalFixtures_WhenDosComUsesComExtension_CopiesOriginalAndBuildsInventory()
    {
        using var temp = new TempDirectory();
        string sourceDir = Path.Combine(temp.Path, "source");
        string artifactsRoot = Path.Combine(temp.Path, "artifacts");
        string packedDir = Path.Combine(artifactsRoot, "upx-packed-fixtures");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(packedDir);

        byte[] dosCom = CreateSyntheticDosCom();
        File.WriteAllBytes(Path.Combine(sourceDir, "sample.com"), dosCom);
        File.WriteAllBytes(Path.Combine(packedDir, "DosCom-sample.com.upx"), Pack(dosCom));

        ProcessResult result = RunImportScript(sourceDir, artifactsRoot, dryRun: false, strict: false);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(Path.Combine(artifactsRoot, "real-fixtures", "DosCom-sample.com")));
        string inventoryPath = Path.Combine(artifactsRoot, "fixture-inventory.json");
        Assert.True(File.Exists(inventoryPath));
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(inventoryPath));
        JsonElement fixture = Assert.Single(document.RootElement.GetProperty("fixtures").EnumerateArray());
        Assert.Equal("DosCom", fixture.GetProperty("format").GetString());
        Assert.Equal("DosCom-sample.com", fixture.GetProperty("path").GetString());
        Assert.Equal("DosCom-sample.com.upx", fixture.GetProperty("packedPath").GetString());
    }

    [Fact]
    public void ImportExternalFixtures_DryRun_DoesNotCopyOrBuildInventory()
    {
        using var temp = new TempDirectory();
        string sourceDir = Path.Combine(temp.Path, "source");
        string artifactsRoot = Path.Combine(temp.Path, "artifacts");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllBytes(Path.Combine(sourceDir, "pe64.exe"), CreateSyntheticPe(0x8664));

        ProcessResult result = RunImportScript(sourceDir, artifactsRoot, dryRun: true, strict: false);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Win64Pe", result.Output);
        Assert.Contains("Dry run", result.Output);
        Assert.False(File.Exists(Path.Combine(artifactsRoot, "fixture-inventory.json")));
        Assert.False(Directory.Exists(Path.Combine(artifactsRoot, "real-fixtures")));
    }

    [Fact]
    public void ImportExternalFixtures_WhenFixtureInsideRepo_SkipsAsExternalFixture()
    {
        using var temp = new TempDirectory();
        string artifactsRoot = Path.Combine(temp.Path, "artifacts");
        string repoLocalDir = Path.Combine(FindRepoRoot(), "bin", "fixture-import-test");
        Directory.CreateDirectory(repoLocalDir);
        string repoLocalFixture = Path.Combine(repoLocalDir, "repo-local-pe32.exe");
        File.WriteAllBytes(repoLocalFixture, CreateSyntheticPe(0x014C));

        try
        {
            ProcessResult result = RunImportScript(repoLocalDir, artifactsRoot, dryRun: false, strict: false);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Repo-local fixture skipped", result.Error + result.Output);
            using JsonDocument inventory = JsonDocument.Parse(File.ReadAllText(Path.Combine(artifactsRoot, "fixture-inventory.json")));
            Assert.Empty(inventory.RootElement.GetProperty("fixtures").EnumerateArray());
        }
        finally
        {
            if (File.Exists(repoLocalFixture))
                File.Delete(repoLocalFixture);
            if (Directory.Exists(repoLocalDir))
                Directory.Delete(repoLocalDir, recursive: true);
        }
    }

    [Fact]
    public void ImportExternalFixtures_StrictDuplicateFormat_FailsClearly()
    {
        using var temp = new TempDirectory();
        string sourceDir = Path.Combine(temp.Path, "source");
        string artifactsRoot = Path.Combine(temp.Path, "artifacts");
        Directory.CreateDirectory(sourceDir);
        byte[] pe32 = CreateSyntheticPe(0x014C);
        File.WriteAllBytes(Path.Combine(sourceDir, "a.exe"), pe32);
        File.WriteAllBytes(Path.Combine(sourceDir, "b.exe"), pe32);

        ProcessResult result = RunImportScript(sourceDir, artifactsRoot, dryRun: false, strict: true);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Duplicate original fixtures", result.Error + result.Output);
    }

    [Fact]
    public void BuildFixtureInventory_WhenFixturesExist_WritesDetectedInventory()
    {
        using var temp = new TempDirectory();
        string realDir = Path.Combine(temp.Path, "real");
        string packedDir = Path.Combine(temp.Path, "packed");
        string outputPath = Path.Combine(temp.Path, "fixture-inventory.json");
        string reportPath = Path.Combine(temp.Path, "fixture-missing-report.json");
        Directory.CreateDirectory(realDir);
        Directory.CreateDirectory(packedDir);

        byte[] pe32 = CreateSyntheticPe(0x014C);
        File.WriteAllBytes(Path.Combine(realDir, "pe32.exe"), pe32);
        File.WriteAllBytes(Path.Combine(packedDir, "pe32.exe.upx"), Pack(pe32));

        ProcessResult result = RunScript(realDir, packedDir, outputPath, reportPath, dryRun: false);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(outputPath));
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(outputPath));
        JsonElement fixture = Assert.Single(document.RootElement.GetProperty("fixtures").EnumerateArray());
        Assert.Equal("Win32Pe", fixture.GetProperty("format").GetString());
        Assert.Equal("pe32.exe", fixture.GetProperty("path").GetString());
        Assert.Equal("pe32.exe.upx", fixture.GetProperty("packedPath").GetString());
        Assert.True(File.Exists(reportPath));
    }

    [Fact]
    public void BuildFixtureInventory_WhenPackedFormatMatchesButFilePairDoesNot_MarksPackedMissing()
    {
        using var temp = new TempDirectory();
        string realDir = Path.Combine(temp.Path, "real");
        string packedDir = Path.Combine(temp.Path, "packed");
        string outputPath = Path.Combine(temp.Path, "fixture-inventory.json");
        string reportPath = Path.Combine(temp.Path, "fixture-missing-report.json");
        Directory.CreateDirectory(realDir);
        Directory.CreateDirectory(packedDir);

        byte[] pe32 = CreateSyntheticPe(0x014C);
        File.WriteAllBytes(Path.Combine(realDir, "original.exe"), pe32);
        File.WriteAllBytes(Path.Combine(packedDir, "unrelated.exe.upx"), Pack(pe32));

        ProcessResult result = RunScript(realDir, packedDir, outputPath, reportPath, dryRun: false);

        Assert.Equal(0, result.ExitCode);
        using JsonDocument inventory = JsonDocument.Parse(File.ReadAllText(outputPath));
        JsonElement fixture = Assert.Single(inventory.RootElement.GetProperty("fixtures").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, fixture.GetProperty("packedPath").ValueKind);

        using JsonDocument report = JsonDocument.Parse(File.ReadAllText(reportPath));
        JsonElement win32 = report.RootElement.EnumerateArray()
            .Single(e => e.GetProperty("format").GetString() == "Win32Pe");
        Assert.True(win32.GetProperty("needsPacked").GetBoolean());
        Assert.Equal("MissingPacked", win32.GetProperty("status").GetString());
    }

    [Fact]
    public void BuildFixtureInventory_DryRun_DoesNotWriteInventory()
    {
        using var temp = new TempDirectory();
        string realDir = Path.Combine(temp.Path, "real");
        string packedDir = Path.Combine(temp.Path, "packed");
        string outputPath = Path.Combine(temp.Path, "fixture-inventory.json");
        string reportPath = Path.Combine(temp.Path, "fixture-missing-report.json");
        Directory.CreateDirectory(realDir);
        Directory.CreateDirectory(packedDir);
        File.WriteAllBytes(Path.Combine(realDir, "pe64.exe"), CreateSyntheticPe(0x8664));

        ProcessResult result = RunScript(realDir, packedDir, outputPath, reportPath, dryRun: true);

        Assert.Equal(0, result.ExitCode);
        Assert.False(File.Exists(outputPath));
        Assert.False(File.Exists(reportPath));
        Assert.Contains("Win64Pe", result.Output);
        Assert.Contains("Dry run", result.Output);
    }

    [Fact]
    public void BuildFixtureInventory_WhenDuplicateFormat_FailsClearly()
    {
        using var temp = new TempDirectory();
        string realDir = Path.Combine(temp.Path, "real");
        string packedDir = Path.Combine(temp.Path, "packed");
        string outputPath = Path.Combine(temp.Path, "fixture-inventory.json");
        string reportPath = Path.Combine(temp.Path, "fixture-missing-report.json");
        Directory.CreateDirectory(realDir);
        Directory.CreateDirectory(packedDir);
        byte[] pe32 = CreateSyntheticPe(0x014C);
        File.WriteAllBytes(Path.Combine(realDir, "a.exe"), pe32);
        File.WriteAllBytes(Path.Combine(realDir, "b.exe"), pe32);

        ProcessResult result = RunScript(realDir, packedDir, outputPath, reportPath, dryRun: false);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Duplicate original fixtures", result.Error + result.Output);
    }

    [Fact]
    public void BuildFixtureInventory_WhenPackedHasNoOriginal_WritesPackedOrphanReport()
    {
        using var temp = new TempDirectory();
        string realDir = Path.Combine(temp.Path, "real");
        string packedDir = Path.Combine(temp.Path, "packed");
        string outputPath = Path.Combine(temp.Path, "fixture-inventory.json");
        string reportPath = Path.Combine(temp.Path, "fixture-missing-report.json");
        Directory.CreateDirectory(realDir);
        Directory.CreateDirectory(packedDir);
        File.WriteAllBytes(Path.Combine(packedDir, "pe64.exe.upx"), Pack(CreateSyntheticPe(0x8664)));

        ProcessResult result = RunScript(realDir, packedDir, outputPath, reportPath, dryRun: false);

        Assert.Equal(0, result.ExitCode);
        using JsonDocument inventory = JsonDocument.Parse(File.ReadAllText(outputPath));
        Assert.Empty(inventory.RootElement.GetProperty("fixtures").EnumerateArray());

        using JsonDocument report = JsonDocument.Parse(File.ReadAllText(reportPath));
        JsonElement win64 = report.RootElement.EnumerateArray()
            .Single(e => e.GetProperty("format").GetString() == "Win64Pe");
        Assert.True(win64.GetProperty("needsOriginal").GetBoolean());
        Assert.False(win64.GetProperty("needsPacked").GetBoolean());
        Assert.Equal("pe64.exe.upx", win64.GetProperty("detectedPacked").GetString());
        Assert.Equal("PackedOrphan", win64.GetProperty("status").GetString());
    }

    [Fact]
    public void BuildFixtureInventory_WhenFixtureInsideRepo_SkipsAsExternalFixture()
    {
        using var temp = new TempDirectory();
        string packedDir = Path.Combine(temp.Path, "packed");
        string outputPath = Path.Combine(temp.Path, "fixture-inventory.json");
        string reportPath = Path.Combine(temp.Path, "fixture-missing-report.json");
        Directory.CreateDirectory(packedDir);

        string repoLocalDir = Path.Combine(FindRepoRoot(), "bin", "fixture-inventory-test");
        Directory.CreateDirectory(repoLocalDir);
        string repoLocalFixture = Path.Combine(repoLocalDir, "repo-local-pe32.exe");
        File.WriteAllBytes(repoLocalFixture, CreateSyntheticPe(0x014C));

        try
        {
            ProcessResult result = RunScript(repoLocalDir, packedDir, outputPath, reportPath, dryRun: false);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Repo-local fixture skipped", result.Error + result.Output);
            using JsonDocument inventory = JsonDocument.Parse(File.ReadAllText(outputPath));
            Assert.Empty(inventory.RootElement.GetProperty("fixtures").EnumerateArray());
        }
        finally
        {
            if (File.Exists(repoLocalFixture))
                File.Delete(repoLocalFixture);
            if (Directory.Exists(repoLocalDir))
                Directory.Delete(repoLocalDir, recursive: true);
        }
    }

    [Fact]
    public void VerifyExternalArtifacts_WhenInventoryPointsInsideRepo_FailsClearly()
    {
        using var temp = new TempDirectory();
        string realDir = Path.Combine(temp.Path, "real");
        string packedDir = Path.Combine(temp.Path, "packed");
        string reportDir = Path.Combine(temp.Path, "reports");
        string inventoryPath = Path.Combine(temp.Path, "fixture-inventory.json");
        Directory.CreateDirectory(realDir);
        Directory.CreateDirectory(packedDir);
        Directory.CreateDirectory(reportDir);

        string repoLocalDir = Path.Combine(FindRepoRoot(), "bin", "fixture-verify-test");
        Directory.CreateDirectory(repoLocalDir);
        string repoLocalFixture = Path.Combine(repoLocalDir, "repo-local-pe32.exe");
        File.WriteAllBytes(repoLocalFixture, CreateSyntheticPe(0x014C));
        File.WriteAllText(inventoryPath, $$"""
        {
          "fixtures": [
            {
              "format": "Win32Pe",
              "path": "{{repoLocalFixture.Replace("\\", "\\\\")}}",
              "packedPath": "{{repoLocalFixture.Replace("\\", "\\\\")}}"
            }
          ]
        }
        """);

        try
        {
            ProcessResult result = RunVerifyScript(realDir, packedDir, inventoryPath, reportDir);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("inside repository", result.Error + result.Output);
        }
        finally
        {
            if (File.Exists(repoLocalFixture))
                File.Delete(repoLocalFixture);
            if (Directory.Exists(repoLocalDir))
                Directory.Delete(repoLocalDir, recursive: true);
        }
    }

    [Fact]
    public void BuildFixtureAcquisitionPlan_WritesSuggestedNamesAndCurrentPackedOrphan()
    {
        using var temp = new TempDirectory();
        string reportPath = Path.Combine(temp.Path, "fixture-missing-report.json");
        string planPath = Path.Combine(temp.Path, "fixture-acquisition-plan.json");
        File.WriteAllText(reportPath, """
        [
          {
            "format": "Win64Pe",
            "needsOriginal": true,
            "needsPacked": false,
            "detectedOriginal": null,
            "detectedPacked": "official-upx-win64.exe.upx",
            "status": "PackedOrphan"
          },
          {
            "format": "MachI386",
            "needsOriginal": true,
            "needsPacked": true,
            "detectedOriginal": null,
            "detectedPacked": null,
            "status": "MissingOriginal"
          }
        ]
        """);

        ProcessResult result = RunAcquisitionPlanScript(reportPath, planPath);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(planPath));
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(planPath));
        JsonElement win64 = document.RootElement.EnumerateArray()
            .Single(e => e.GetProperty("format").GetString() == "Win64Pe");
        Assert.True(win64.GetProperty("needsOriginal").GetBoolean());
        Assert.False(win64.GetProperty("needsPacked").GetBoolean());
        Assert.Equal("win64pe-original.bin", win64.GetProperty("suggestedOriginalName").GetString());
        Assert.Equal("official-upx-win64.exe.upx", win64.GetProperty("currentPackedOrphan").GetString());
    }

    [Fact]
    public void BuildFixtureAcquisitionPlan_IncludesCandidateStatusWhenReportExists()
    {
        using var temp = new TempDirectory();
        string reportPath = Path.Combine(temp.Path, "fixture-missing-report.json");
        string candidatePath = Path.Combine(temp.Path, "real-fixture-candidates-report.json");
        string planPath = Path.Combine(temp.Path, "fixture-acquisition-plan.json");
        File.WriteAllText(reportPath, """
        [
          {
            "format": "DosCom",
            "needsOriginal": true,
            "needsPacked": true,
            "detectedOriginal": null,
            "detectedPacked": null,
            "status": "MissingOriginal"
          }
        ]
        """);
        File.WriteAllText(candidatePath, """
        [
          {
            "format": "DosCom",
            "sourceName": "sample dos com",
            "status": "BlockedByScaffold"
          }
        ]
        """);

        ProcessResult result = RunAcquisitionPlanScript(reportPath, planPath, candidatePath);

        Assert.Equal(0, result.ExitCode);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(planPath));
        JsonElement item = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("BlockedByScaffold", item.GetProperty("candidateStatus").GetString());
        Assert.Equal("sample dos com", item.GetProperty("candidateSourceName").GetString());
    }

    [Fact]
    public void MaterializeOfficialOrphanFixtures_WhenUpxRestoresExpectedFormat_WritesOriginalAndReport()
    {
        using var temp = new TempDirectory();
        string artifactsRoot = Path.Combine(temp.Path, "artifacts");
        string packedDir = Path.Combine(artifactsRoot, "packed");
        string realDir = Path.Combine(artifactsRoot, "real");
        string reportPath = Path.Combine(artifactsRoot, "fixture-missing-report.json");
        string materializedReportPath = Path.Combine(artifactsRoot, "materialized-report.json");
        string fakeUpx = CreateFakeUpxScript(temp.Path);
        Directory.CreateDirectory(packedDir);
        byte[] pe32 = CreateSyntheticPe(0x014C);
        File.WriteAllBytes(Path.Combine(packedDir, "pe32.upx"), pe32);
        WritePackedOrphanReport(reportPath, "Win32Pe", "pe32.upx");

        ProcessResult result = RunMaterializeOrphansScript(artifactsRoot, reportPath, realDir, packedDir, fakeUpx, materializedReportPath);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(Path.Combine(realDir, "Win32Pe-official-restored-pe32")));
        using JsonDocument report = JsonDocument.Parse(File.ReadAllText(materializedReportPath));
        JsonElement item = Assert.Single(report.RootElement.EnumerateArray());
        Assert.Equal("Materialized", item.GetProperty("status").GetString());
        Assert.Equal("Win32Pe", item.GetProperty("detectedFormat").GetString());
    }

    [Fact]
    public void MaterializeOfficialOrphanFixtures_WhenRestoredFormatDiffers_RejectsOriginal()
    {
        using var temp = new TempDirectory();
        string artifactsRoot = Path.Combine(temp.Path, "artifacts");
        string packedDir = Path.Combine(artifactsRoot, "packed");
        string realDir = Path.Combine(artifactsRoot, "real");
        string reportPath = Path.Combine(artifactsRoot, "fixture-missing-report.json");
        string materializedReportPath = Path.Combine(artifactsRoot, "materialized-report.json");
        string fakeUpx = CreateFakeUpxScript(temp.Path);
        Directory.CreateDirectory(packedDir);
        File.WriteAllBytes(Path.Combine(packedDir, "pe64.upx"), CreateSyntheticPe(0x8664));
        WritePackedOrphanReport(reportPath, "Win32Pe", "pe64.upx");

        ProcessResult result = RunMaterializeOrphansScript(artifactsRoot, reportPath, realDir, packedDir, fakeUpx, materializedReportPath);

        Assert.Equal(0, result.ExitCode);
        Assert.False(File.Exists(Path.Combine(realDir, "Win32Pe-official-restored-pe64")));
        using JsonDocument report = JsonDocument.Parse(File.ReadAllText(materializedReportPath));
        JsonElement item = Assert.Single(report.RootElement.EnumerateArray());
        Assert.Equal("DetectedWrongFormat", item.GetProperty("status").GetString());
        Assert.Contains("Expected Win32Pe", item.GetProperty("error").GetString());
    }

    [Fact]
    public void FetchRealFixtureCandidates_WithLocalExternalSource_CopiesRecognizedCandidate()
    {
        using var temp = new TempDirectory();
        string sourceDir = Path.Combine(temp.Path, "external-source");
        string artifactsRoot = Path.Combine(temp.Path, "artifacts");
        string manifestPath = Path.Combine(temp.Path, "fixture-candidate-sources.json");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllBytes(Path.Combine(sourceDir, "pe32.exe"), CreateSyntheticPe(0x014C));
        WriteCandidateManifest(manifestPath, "Win32Pe", sourceDir);

        ProcessResult result = RunFetchCandidatesScript(manifestPath, artifactsRoot, dryRun: false, strict: false);

        Assert.Equal(0, result.ExitCode);
        string incomingFile = Path.Combine(artifactsRoot, "incoming-real-binaries", "Win32Pe-pe32.exe");
        Assert.True(File.Exists(incomingFile));
        string reportPath = Path.Combine(artifactsRoot, "real-fixture-candidates-report.json");
        Assert.True(File.Exists(reportPath));
        using JsonDocument report = JsonDocument.Parse(File.ReadAllText(reportPath));
        JsonElement item = Assert.Single(report.RootElement.EnumerateArray());
        Assert.Equal("Win32Pe", item.GetProperty("detectedFormat").GetString());
        Assert.Equal("CopiedRecognized", item.GetProperty("status").GetString());
        Assert.Equal("test-only", item.GetProperty("license").GetString());
        Assert.Equal(sourceDir, item.GetProperty("source").GetString());
    }

    [Fact]
    public void FetchRealFixtureCandidates_DryRun_DoesNotCopyOrWriteReport()
    {
        using var temp = new TempDirectory();
        string sourceDir = Path.Combine(temp.Path, "external-source");
        string artifactsRoot = Path.Combine(temp.Path, "artifacts");
        string manifestPath = Path.Combine(temp.Path, "fixture-candidate-sources.json");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllBytes(Path.Combine(sourceDir, "pe64.exe"), CreateSyntheticPe(0x8664));
        WriteCandidateManifest(manifestPath, "Win64Pe", sourceDir);

        ProcessResult result = RunFetchCandidatesScript(manifestPath, artifactsRoot, dryRun: true, strict: false);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("WouldCopyRecognized", result.Output);
        Assert.Contains("Dry run", result.Output);
        Assert.False(Directory.Exists(Path.Combine(artifactsRoot, "incoming-real-binaries")));
        Assert.False(File.Exists(Path.Combine(artifactsRoot, "real-fixture-candidates-report.json")));
    }

    [Fact]
    public void FetchRealFixtureCandidates_WhenSourceInsideRepo_SkipsCandidate()
    {
        using var temp = new TempDirectory();
        string artifactsRoot = Path.Combine(temp.Path, "artifacts");
        string manifestPath = Path.Combine(temp.Path, "fixture-candidate-sources.json");
        string repoLocalDir = Path.Combine(FindRepoRoot(), "bin", "fixture-candidate-test");
        Directory.CreateDirectory(repoLocalDir);
        string repoLocalFixture = Path.Combine(repoLocalDir, "repo-local-pe32.exe");
        File.WriteAllBytes(repoLocalFixture, CreateSyntheticPe(0x014C));
        WriteCandidateManifest(manifestPath, "Win32Pe", repoLocalDir);

        try
        {
            ProcessResult result = RunFetchCandidatesScript(manifestPath, artifactsRoot, dryRun: false, strict: false);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Repo-local candidate skipped", result.Error + result.Output);
            Assert.False(Directory.Exists(Path.Combine(artifactsRoot, "incoming-real-binaries")));
            using JsonDocument report = JsonDocument.Parse(File.ReadAllText(Path.Combine(artifactsRoot, "real-fixture-candidates-report.json")));
            JsonElement item = Assert.Single(report.RootElement.EnumerateArray());
            Assert.Equal("RepoLocalSkipped", item.GetProperty("status").GetString());
        }
        finally
        {
            if (File.Exists(repoLocalFixture))
                File.Delete(repoLocalFixture);
            if (Directory.Exists(repoLocalDir))
                Directory.Delete(repoLocalDir, recursive: true);
        }
    }

    [Fact]
    public void FetchRealFixtureCandidates_WhenMachFatCandidate_ReportsFatMachUnsupported()
    {
        using var temp = new TempDirectory();
        string sourceDir = Path.Combine(temp.Path, "external-source");
        string artifactsRoot = Path.Combine(temp.Path, "artifacts");
        string manifestPath = Path.Combine(temp.Path, "fixture-candidate-sources.json");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllBytes(Path.Combine(sourceDir, "fat-mach"), [0xCA, 0xFE, 0xBA, 0xBE, 0x00, 0x00, 0x00, 0x01]);
        WriteCandidateManifest(manifestPath, "MachAMD64", sourceDir);

        ProcessResult result = RunFetchCandidatesScript(manifestPath, artifactsRoot, dryRun: false, strict: false);

        Assert.Equal(0, result.ExitCode);
        using JsonDocument report = JsonDocument.Parse(File.ReadAllText(Path.Combine(artifactsRoot, "real-fixture-candidates-report.json")));
        JsonElement item = Assert.Single(report.RootElement.EnumerateArray());
        Assert.Equal("FatMachUnsupported", item.GetProperty("status").GetString());
    }

    [Fact]
    public void FetchRealFixtureCandidates_WhenMachFatContainsExpectedSlice_ExtractsAndCopiesRecognizedSlice()
    {
        using var temp = new TempDirectory();
        string sourceDir = Path.Combine(temp.Path, "external-source");
        string artifactsRoot = Path.Combine(temp.Path, "artifacts");
        string manifestPath = Path.Combine(temp.Path, "fixture-candidate-sources.json");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllBytes(Path.Combine(sourceDir, "universal-tool"), CreateFatMachO(CreateSyntheticMach64()));
        WriteCandidateManifest(manifestPath, "MachAMD64", sourceDir);

        ProcessResult result = RunFetchCandidatesScript(manifestPath, artifactsRoot, dryRun: false, strict: false);

        Assert.Equal(0, result.ExitCode);
        string incomingDir = Path.Combine(artifactsRoot, "incoming-real-binaries");
        string reportText = File.Exists(Path.Combine(artifactsRoot, "real-fixture-candidates-report.json"))
            ? File.ReadAllText(Path.Combine(artifactsRoot, "real-fixture-candidates-report.json"))
            : "<missing report>";
        Assert.True(Directory.Exists(incomingDir), result.Output + result.Error + reportText);
        Assert.Contains(Directory.GetFiles(incomingDir), path => Path.GetFileName(path).StartsWith("MachAMD64-", StringComparison.Ordinal));
        using JsonDocument report = JsonDocument.Parse(File.ReadAllText(Path.Combine(artifactsRoot, "real-fixture-candidates-report.json")));
        Assert.Contains(report.RootElement.EnumerateArray(), item => item.GetProperty("status").GetString() == "ExtractedFatMachSlice");
        Assert.Contains(report.RootElement.EnumerateArray(), item => item.GetProperty("status").GetString() == "CopiedRecognized");
    }

    [Fact]
    public void FetchRealFixtureCandidates_WhenRealDosComCandidate_IsCopiedRecognized()
    {
        using var temp = new TempDirectory();
        string sourceDir = Path.Combine(temp.Path, "external-source");
        string artifactsRoot = Path.Combine(temp.Path, "artifacts");
        string manifestPath = Path.Combine(temp.Path, "fixture-candidate-sources.json");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllBytes(Path.Combine(sourceDir, "sample.com"), [0xB4, 0x09, 0xCD, 0x21]);
        WriteCandidateManifest(manifestPath, "DosCom", sourceDir);

        ProcessResult result = RunFetchCandidatesScript(manifestPath, artifactsRoot, dryRun: false, strict: false);

        Assert.Equal(0, result.ExitCode);
        using JsonDocument report = JsonDocument.Parse(File.ReadAllText(Path.Combine(artifactsRoot, "real-fixture-candidates-report.json")));
        JsonElement item = Assert.Single(report.RootElement.EnumerateArray());
        Assert.Equal("DosCom", item.GetProperty("detectedFormat").GetString());
        Assert.Equal("CopiedRecognized", item.GetProperty("status").GetString());
        Assert.True(File.Exists(Path.Combine(artifactsRoot, "incoming-real-binaries", "DosCom-sample.com")));
    }

    [Fact]
    public void FetchRealFixtureCandidates_WhenManifestMissing_CreatesTemplateAndBlocks()
    {
        using var temp = new TempDirectory();
        string artifactsRoot = Path.Combine(temp.Path, "artifacts");
        string manifestPath = Path.Combine(artifactsRoot, "fixture-candidate-sources.json");

        ProcessResult result = RunFetchCandidatesScript(manifestPath, artifactsRoot, dryRun: false, strict: false);

        Assert.NotEqual(0, result.ExitCode);
        Assert.True(File.Exists(manifestPath));
        Assert.Contains("Created candidate source template", result.Output);
        using JsonDocument template = JsonDocument.Parse(File.ReadAllText(manifestPath));
        JsonElement.ArrayEnumerator sources = template.RootElement.GetProperty("sources").EnumerateArray();
        Assert.Equal(18, sources.Count());
        Assert.Contains(template.RootElement.GetProperty("sources").EnumerateArray(), source => source.GetProperty("format").GetString() == "Win32Pe");
        Assert.DoesNotContain(template.RootElement.GetProperty("sources").EnumerateArray(), source => source.GetProperty("format").GetString()!.Contains(' '));
    }

    [Fact]
    public void PopulateFixtureCandidateSources_WithoutNetwork_RecordsHonestUnresolvedSources()
    {
        using var temp = new TempDirectory();
        string outputPath = Path.Combine(temp.Path, "fixture-candidate-sources.json");

        ProcessResult result = RunPopulateCandidatesScript(temp.Path, outputPath);

        Assert.Equal(0, result.ExitCode);
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(outputPath));
        JsonElement[] sources = manifest.RootElement.GetProperty("sources").EnumerateArray().ToArray();
        Assert.Equal(18, sources.Length);

        JsonElement dosCom = Assert.Single(sources, source => source.GetProperty("format").GetString() == "DosCom");
        Assert.Equal(JsonValueKind.Null, dosCom.GetProperty("url").ValueKind);
        Assert.Contains("FreeDOS", dosCom.GetProperty("notes").GetString());

        JsonElement atari = Assert.Single(sources, source => source.GetProperty("format").GetString() == "AtariTos");
        Assert.Contains("FreeMiNT", atari.GetProperty("notes").GetString());

        JsonElement vmlinuz = Assert.Single(sources, source => source.GetProperty("format").GetString() == "Vmlinuzi386");
        Assert.Equal("UNRESOLVED-Vmlinuzi386", vmlinuz.GetProperty("sourceName").GetString());
    }

    private static ProcessResult RunScript(string realDir, string packedDir, string outputPath, string reportPath, bool dryRun)
    {
        string script = Path.Combine(FindRepoRoot(), "tools", "build-fixture-inventory.ps1");
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(script);
        startInfo.ArgumentList.Add("-RealFixturesDir");
        startInfo.ArgumentList.Add(realDir);
        startInfo.ArgumentList.Add("-UpxPackedFixturesDir");
        startInfo.ArgumentList.Add(packedDir);
        startInfo.ArgumentList.Add("-OutputPath");
        startInfo.ArgumentList.Add(outputPath);
        startInfo.ArgumentList.Add("-MissingReportPath");
        startInfo.ArgumentList.Add(reportPath);
        if (dryRun)
            startInfo.ArgumentList.Add("-DryRun");

        using Process process = Process.Start(startInfo)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, output, error);
    }

    private static ProcessResult RunVerifyScript(string realDir, string packedDir, string inventoryPath, string reportDir)
    {
        string script = Path.Combine(FindRepoRoot(), "tools", "verify-external-artifacts.ps1");
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(script);
        startInfo.ArgumentList.Add("-RealFixturesDir");
        startInfo.ArgumentList.Add(realDir);
        startInfo.ArgumentList.Add("-UpxPackedFixturesDir");
        startInfo.ArgumentList.Add(packedDir);
        startInfo.ArgumentList.Add("-FixtureInventoryPath");
        startInfo.ArgumentList.Add(inventoryPath);
        startInfo.ArgumentList.Add("-ReportDir");
        startInfo.ArgumentList.Add(reportDir);

        using Process process = Process.Start(startInfo)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, output, error);
    }

    private static ProcessResult RunImportScript(string sourceDir, string artifactsRoot, bool dryRun, bool strict)
    {
        string script = Path.Combine(FindRepoRoot(), "tools", "import-external-fixtures.ps1");
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(script);
        startInfo.ArgumentList.Add("-SourceDir");
        startInfo.ArgumentList.Add(sourceDir);
        startInfo.ArgumentList.Add("-ArtifactsRoot");
        startInfo.ArgumentList.Add(artifactsRoot);
        if (dryRun)
            startInfo.ArgumentList.Add("-DryRun");
        if (strict)
            startInfo.ArgumentList.Add("-Strict");

        using Process process = Process.Start(startInfo)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, output, error);
    }

    private static ProcessResult RunFetchCandidatesScript(string manifestPath, string artifactsRoot, bool dryRun, bool strict)
    {
        string script = Path.Combine(FindRepoRoot(), "tools", "fetch-real-fixture-candidates.ps1");
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(script);
        startInfo.ArgumentList.Add("-CandidateManifestPath");
        startInfo.ArgumentList.Add(manifestPath);
        startInfo.ArgumentList.Add("-ArtifactsRoot");
        startInfo.ArgumentList.Add(artifactsRoot);
        if (dryRun)
            startInfo.ArgumentList.Add("-DryRun");
        if (strict)
            startInfo.ArgumentList.Add("-Strict");

        using Process process = Process.Start(startInfo)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, output, error);
    }

    private static ProcessResult RunPopulateCandidatesScript(string artifactsRoot, string outputPath)
    {
        string script = Path.Combine(FindRepoRoot(), "tools", "populate-fixture-candidate-sources.ps1");
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(script);
        startInfo.ArgumentList.Add("-ArtifactsRoot");
        startInfo.ArgumentList.Add(artifactsRoot);
        startInfo.ArgumentList.Add("-OutputPath");
        startInfo.ArgumentList.Add(outputPath);
        startInfo.ArgumentList.Add("-NoNetwork");

        using Process process = Process.Start(startInfo)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, output, error);
    }

    private static ProcessResult RunMaterializeOrphansScript(
        string artifactsRoot,
        string missingReportPath,
        string realFixturesDir,
        string packedFixturesDir,
        string upxExePath,
        string outputReportPath)
    {
        string script = Path.Combine(FindRepoRoot(), "tools", "materialize-official-orphan-fixtures.ps1");
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(script);
        startInfo.ArgumentList.Add("-ArtifactsRoot");
        startInfo.ArgumentList.Add(artifactsRoot);
        startInfo.ArgumentList.Add("-MissingReportPath");
        startInfo.ArgumentList.Add(missingReportPath);
        startInfo.ArgumentList.Add("-RealFixturesDir");
        startInfo.ArgumentList.Add(realFixturesDir);
        startInfo.ArgumentList.Add("-UpxPackedFixturesDir");
        startInfo.ArgumentList.Add(packedFixturesDir);
        startInfo.ArgumentList.Add("-UpxExePath");
        startInfo.ArgumentList.Add(upxExePath);
        startInfo.ArgumentList.Add("-OutputReportPath");
        startInfo.ArgumentList.Add(outputReportPath);

        using Process process = Process.Start(startInfo)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, output, error);
    }

    private static ProcessResult RunAcquisitionPlanScript(string reportPath, string planPath, string? candidateReportPath = null)
    {
        string script = Path.Combine(FindRepoRoot(), "tools", "build-fixture-acquisition-plan.ps1");
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(script);
        startInfo.ArgumentList.Add("-MissingReportPath");
        startInfo.ArgumentList.Add(reportPath);
        startInfo.ArgumentList.Add("-OutputPath");
        startInfo.ArgumentList.Add(planPath);
        if (candidateReportPath is not null)
        {
            startInfo.ArgumentList.Add("-CandidateReportPath");
            startInfo.ArgumentList.Add(candidateReportPath);
        }

        using Process process = Process.Start(startInfo)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, output, error);
    }

    private static void WriteCandidateManifest(string manifestPath, string format, string localPath)
    {
        string escapedPath = localPath.Replace("\\", "\\\\");
        File.WriteAllText(manifestPath, $$"""
        {
          "sources": [
            {
              "format": "{{format}}",
              "sourceName": "{{format}} local test",
              "localPath": "{{escapedPath}}",
              "url": null,
              "archivePath": null,
              "fileGlobs": [ "*" ],
              "license": "test-only",
              "notes": "test fixture outside repo"
            }
          ]
        }
        """);
    }

    private static void WritePackedOrphanReport(string reportPath, string format, string packedPath)
    {
        File.WriteAllText(reportPath, $$"""
        [
          {
            "format": "{{format}}",
            "needsOriginal": true,
            "needsPacked": false,
            "detectedOriginal": null,
            "detectedPacked": "{{packedPath}}",
            "status": "PackedOrphan"
          }
        ]
        """);
    }

    private static string CreateFakeUpxScript(string directory)
    {
        string path = Path.Combine(directory, "fake-upx.cmd");
        File.WriteAllText(path, """
        @echo off
        if "%1"=="-d" exit /b 0
        exit /b 1
        """);
        return path;
    }

    private static byte[] Pack(byte[] original)
    {
        using var input = TempFile.Create(original);
        using var packed = TempFile.CreateEmpty();
        var options = new Options { Method = CompressionMethod.Lzma, Level = 9 };

        using (var inputFile = new InputFile(input.Path))
        using (var outputFile = new OutputFile(packed.Path, overwrite: true))
            PackMaster.FindPacker(inputFile, options).DoPack(outputFile, options);

        return File.ReadAllBytes(packed.Path);
    }

    private static byte[] CreateSyntheticDosCom()
    {
        byte[] data = new byte[0x80];
        "COM!"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x10), 0x20);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x14), 0x20);
        "UPXSharp DOS COM fixture"u8.CopyTo(data.AsSpan(0x20));
        return data;
    }

    private static byte[] CreateFatMachO(byte[] slice)
    {
        const int sliceOffset = 0x100;
        byte[] data = new byte[sliceOffset + slice.Length];
        data[0] = 0xCA;
        data[1] = 0xFE;
        data[2] = 0xBA;
        data[3] = 0xBE;
        WriteUInt32BigEndian(data.AsSpan(4), 1);
        WriteUInt32BigEndian(data.AsSpan(8), 0x01000007);
        WriteUInt32BigEndian(data.AsSpan(12), 3);
        WriteUInt32BigEndian(data.AsSpan(16), sliceOffset);
        WriteUInt32BigEndian(data.AsSpan(20), (uint)slice.Length);
        WriteUInt32BigEndian(data.AsSpan(24), 0);
        slice.CopyTo(data.AsSpan(sliceOffset));
        return data;
    }

    private static byte[] CreateSyntheticMach64()
    {
        byte[] data = new byte[0x140];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0), 0xFEEDFACF);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 0x01000007);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20), 152);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(24), 0);

        int command = 32;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(command), 0x19);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(command + 4), 152);
        Encoding.ASCII.GetBytes("__TEXT").CopyTo(data.AsSpan(command + 8));
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(command + 24), 0x1000);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(command + 32), 0x1000);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(command + 40), 0);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(command + 48), (ulong)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(command + 64), 1);

        int section = command + 72;
        Encoding.ASCII.GetBytes("__text").CopyTo(data.AsSpan(section));
        Encoding.ASCII.GetBytes("__TEXT").CopyTo(data.AsSpan(section + 16));
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(section + 32), 0x1100);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(section + 40), 0x20);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(section + 48), 0x100);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(section + 64), 0x80000400);
        "UPXSharp Mach fixture"u8.CopyTo(data.AsSpan(0x100));
        return data;
    }

    private static byte[] CreateSyntheticPe(ushort machine)
    {
        bool is64 = machine == 0x8664;
        byte[] data = new byte[0x10000];
        data[0] = (byte)'M';
        data[1] = (byte)'Z';
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(60), 0x80);

        int peOffset = 0x80;
        data[peOffset] = (byte)'P';
        data[peOffset + 1] = (byte)'E';
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(peOffset + 4), machine);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(peOffset + 6), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(peOffset + 20), is64 ? (ushort)0xF0 : (ushort)0xE0);

        int optional = peOffset + 24;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(optional), is64 ? (ushort)0x20B : (ushort)0x10B);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(optional + 16), 0x1000);
        if (is64)
            BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(optional + 24), 0x140000000);
        else
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(optional + 28), 0x400000);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(optional + 60), 0x200);

        int section = optional + (is64 ? 0xF0 : 0xE0);
        Encoding.ASCII.GetBytes(".text\0\0\0").CopyTo(data.AsSpan(section, 8));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(section + 8), 0x40);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(section + 12), 0x1000);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(section + 16), 0x40);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(section + 20), 0x200);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(section + 36), 0x60000020);
        "UPXSharp fixture inventory test"u8.CopyTo(data.AsSpan(0x200));
        return data;
    }

    private static void WriteUInt32BigEndian(Span<byte> destination, uint value)
    {
        destination[0] = (byte)(value >> 24);
        destination[1] = (byte)(value >> 16);
        destination[2] = (byte)(value >> 8);
        destination[3] = (byte)value;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "UpxSharp.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Repo root not found");
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);

    private sealed class TempFile : IDisposable
    {
        private TempFile(string path) => Path = path;

        public string Path { get; }

        public static TempFile Create(byte[] data)
        {
            var file = CreateEmpty();
            File.WriteAllBytes(file.Path, data);
            return file;
        }

        public static TempFile CreateEmpty()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"upxsharp-fixture-inventory-{Guid.NewGuid():N}.bin");
            return new TempFile(path);
        }

        public void Dispose()
        {
            if (File.Exists(Path))
                File.Delete(Path);
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "upxsharp-fixture-inventory-" + Guid.NewGuid().ToString("N"));
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
