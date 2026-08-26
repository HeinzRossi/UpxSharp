using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using UpxSharp.Core;
using UpxSharp.IO;
using UpxSharp.Packers;
using UpxSharp.Official;
using Xunit;

namespace UpxSharp.Tests;

public class RealFixtureTests
{
    private const long MaxFixtureSize = 32L * 1024 * 1024;
    private const string RealFixturesEnv = "UPXSHARP_REAL_FIXTURES_DIR";
    private const string PackedFixturesEnv = "UPXSHARP_UPX_PACKED_FIXTURES_DIR";
    private const string FixtureInventoryEnv = "UPXSHARP_REAL_FIXTURES_INVENTORY";
    private const string StrictEnv = "UPXSHARP_REAL_FIXTURES_STRICT";
    private const string ReportDirEnv = "UPXSHARP_REAL_FIXTURES_REPORT_DIR";

    [Fact]
    public void RealFixtures_PackThenUnpack_WhenDirectoryConfigured_WritesReport()
    {
        string? fixtureDir = Environment.GetEnvironmentVariable(RealFixturesEnv);
        if (!IsUsableDirectory(fixtureDir))
            return;

        FixtureInventory? inventory = LoadInventoryOrNull();
        var reports = RunOriginalFixtureReport(fixtureDir!, inventory).ToList();
        WriteReport("upxsharp-real-fixtures-report.json", reports);
        WriteFormatReport("upxsharp-real-fixtures-format-report.json", BuildFormatReports(inventory, reports, packedReports: null).ToArray());
        EnforceStrict(inventory, reports, packedReports: null);
    }

    [Fact]
    public void UpxPackedFixtures_FindUnpacker_WhenDirectoryConfigured_WritesReport()
    {
        string? fixtureDir = Environment.GetEnvironmentVariable(PackedFixturesEnv);
        if (!IsUsableDirectory(fixtureDir))
            return;

        FixtureInventory? inventory = LoadInventoryOrNull();
        var reports = RunPackedFixtureReport(fixtureDir!, inventory).ToList();
        WriteReport("upxsharp-upx-packed-fixtures-report.json", reports);
        WriteFormatReport("upxsharp-upx-packed-fixtures-format-report.json", BuildFormatReports(inventory, originalReports: null, reports).ToArray());
        EnforceStrict(inventory, originalReports: null, reports);
    }

    [Fact]
    public void RealFixturesReport_WithSyntheticDirectory_WritesExpectedStatuses()
    {
        using var temp = new TempDirectory();
        string fixtureDir = Path.Combine(temp.Path, "fixtures");
        string reportDir = Path.Combine(temp.Path, "reports");
        Directory.CreateDirectory(fixtureDir);
        Directory.CreateDirectory(reportDir);

        File.WriteAllBytes(Path.Combine(fixtureDir, "valid.exe"), GetSyntheticFixture());
        File.WriteAllText(Path.Combine(fixtureDir, "unknown.bin"), "not a known executable");

        using var env = EnvScope.Set(
            (RealFixturesEnv, fixtureDir),
            (ReportDirEnv, reportDir));

        RealFixtures_PackThenUnpack_WhenDirectoryConfigured_WritesReport();

        string reportPath = Path.Combine(reportDir, "upxsharp-real-fixtures-report.json");
        Assert.True(File.Exists(reportPath));
        RealFixtureReport[] reports = ReadReports(reportPath);

        Assert.Contains(reports, r => r.File.EndsWith("valid.exe", StringComparison.OrdinalIgnoreCase)
            && r.Status == RealFixtureStatus.PackedRoundTripOk
            && !string.IsNullOrWhiteSpace(r.DetectedFormat));
        Assert.Contains(reports, r => r.File.EndsWith("unknown.bin", StringComparison.OrdinalIgnoreCase)
            && r.Status == RealFixtureStatus.NotRecognized
            && r.DetectedFormat is null);
    }

    [Fact]
    public void UpxPackedFixturesReport_WithSyntheticDirectory_WritesNotPackedStatus()
    {
        using var temp = new TempDirectory();
        string fixtureDir = Path.Combine(temp.Path, "fixtures");
        string reportDir = Path.Combine(temp.Path, "reports");
        Directory.CreateDirectory(fixtureDir);
        Directory.CreateDirectory(reportDir);

        File.WriteAllBytes(Path.Combine(fixtureDir, "valid.exe"), GetSyntheticFixture());

        using var env = EnvScope.Set(
            (PackedFixturesEnv, fixtureDir),
            (ReportDirEnv, reportDir));

        UpxPackedFixtures_FindUnpacker_WhenDirectoryConfigured_WritesReport();

        string reportPath = Path.Combine(reportDir, "upxsharp-upx-packed-fixtures-report.json");
        Assert.True(File.Exists(reportPath));
        RealFixtureReport[] reports = ReadReports(reportPath);

        Assert.Contains(reports, r => r.File.EndsWith("valid.exe", StringComparison.OrdinalIgnoreCase)
            && r.Status == RealFixtureStatus.NotPacked);
    }

    [Fact]
    public void OfficialElfUnsupportedLayout_HasDedicatedReportStatus()
    {
        Assert.Equal(
            RealFixtureStatus.OfficialElfUnsupportedLayout,
            GetOfficialFailureStatus(new OfficialUpxUnsupportedLayoutException("shared library")));
        Assert.Equal(
            RealFixtureStatus.OfficialUpxMetadataError,
            GetOfficialFailureStatus(new OfficialUpxMetadataException("truncated p_info")));
        Assert.Equal(
            RealFixtureStatus.OfficialUpxDecompressError,
            GetOfficialFailureStatus(new OfficialUpxDecompressException("invalid lookbehind")));
    }

    [Fact]
    public void StrictMode_WhenRecognizedOriginalDoesNotRoundTrip_Fails()
    {
        var inventory = new FixtureInventory(
        [
            new FixtureInventoryEntry("Win32Pe", "broken.exe", null),
        ]);
        var reports = new[]
        {
            new RealFixtureReport("broken.exe", 100, "Win32Pe", "Win32Pe", RealFixtureStatus.Failed, "boom"),
        };

        using var env = EnvScope.Set((StrictEnv, "1"));

        Assert.ThrowsAny<Exception>(() => EnforceStrict(inventory, reports, packedReports: null));
    }

    [Fact]
    public void StrictMode_WithInventoryMissingExpectedFormat_Fails()
    {
        var inventory = new FixtureInventory(
        [
            new FixtureInventoryEntry("Win32Pe", "valid.exe", null),
        ]);
        var reports = new[]
        {
            new RealFixtureReport("valid.exe", 100, "Win32Pe", "Win32Pe", RealFixtureStatus.PackedRoundTripOk, null),
        };

        using var env = EnvScope.Set((StrictEnv, "1"));

        Assert.ThrowsAny<Exception>(() => EnforceStrict(inventory, reports, packedReports: null));
    }

    [Fact]
    public void StrictMode_WithCompleteSyntheticInventory_PassesOriginalRoundTrip()
    {
        using var temp = new TempDirectory();
        string fixtureDir = Path.Combine(temp.Path, "fixtures");
        string reportDir = Path.Combine(temp.Path, "reports");
        string inventoryPath = Path.Combine(temp.Path, "fixture-inventory.json");
        Directory.CreateDirectory(fixtureDir);
        Directory.CreateDirectory(reportDir);

        WriteCompleteSyntheticInventory(fixtureDir, inventoryPath, includePackedPaths: false);

        using var env = EnvScope.Set(
            (RealFixturesEnv, fixtureDir),
            (FixtureInventoryEnv, inventoryPath),
            (ReportDirEnv, reportDir),
            (StrictEnv, "1"));

        RealFixtures_PackThenUnpack_WhenDirectoryConfigured_WritesReport();

        string reportPath = Path.Combine(reportDir, "upxsharp-real-fixtures-format-report.json");
        Assert.True(File.Exists(reportPath));
        FixtureFormatReport[] reports = ReadFormatReports(reportPath);
        Assert.All(reports, r => Assert.True(r.Covered && r.Recognized && r.RoundTripOk));
    }

    [Fact]
    public void StrictMode_WithCompleteSyntheticPackedInventory_PassesPackedUnpack()
    {
        using var temp = new TempDirectory();
        string fixtureDir = Path.Combine(temp.Path, "fixtures");
        string packedDir = Path.Combine(temp.Path, "packed");
        string reportDir = Path.Combine(temp.Path, "reports");
        string inventoryPath = Path.Combine(temp.Path, "fixture-inventory.json");
        Directory.CreateDirectory(fixtureDir);
        Directory.CreateDirectory(packedDir);
        Directory.CreateDirectory(reportDir);

        WriteCompleteSyntheticInventory(fixtureDir, inventoryPath, includePackedPaths: true);
        foreach ((string format, byte[] bytes) in SyntheticFixturesByFormat())
            File.WriteAllBytes(Path.Combine(packedDir, format + ".bin.upx"), PackSynthetic(bytes));

        using var env = EnvScope.Set(
            (RealFixturesEnv, fixtureDir),
            (PackedFixturesEnv, packedDir),
            (FixtureInventoryEnv, inventoryPath),
            (ReportDirEnv, reportDir),
            (StrictEnv, "1"));

        UpxPackedFixtures_FindUnpacker_WhenDirectoryConfigured_WritesReport();

        string reportPath = Path.Combine(reportDir, "upxsharp-upx-packed-fixtures-format-report.json");
        Assert.True(File.Exists(reportPath));
        FixtureFormatReport[] reports = ReadFormatReports(reportPath);
        Assert.All(reports, r => Assert.True(r.Covered && r.Recognized && r.UpxPackedUnpackOk));
    }

    [Fact]
    public void UpxPackedFixtures_WhenRestoredBytesDifferFromInventoryOriginal_Fails()
    {
        using var temp = new TempDirectory();
        string fixtureDir = Path.Combine(temp.Path, "fixtures");
        string packedDir = Path.Combine(temp.Path, "packed");
        string reportDir = Path.Combine(temp.Path, "reports");
        string inventoryPath = Path.Combine(temp.Path, "fixture-inventory.json");
        Directory.CreateDirectory(fixtureDir);
        Directory.CreateDirectory(packedDir);
        Directory.CreateDirectory(reportDir);

        byte[] original = GetSyntheticFixture();
        File.WriteAllBytes(Path.Combine(fixtureDir, "original.bin"), original);
        File.WriteAllBytes(Path.Combine(packedDir, "packed.bin.upx"), PackSynthetic(original));
        File.WriteAllBytes(Path.Combine(fixtureDir, "original.bin"), [.. original, 0xFF]);

        var inventory = new FixtureInventory(
        [
            new FixtureInventoryEntry(nameof(UpxFormat.Win32Pe), "original.bin", "packed.bin.upx"),
        ]);
        File.WriteAllText(inventoryPath, JsonSerializer.Serialize(inventory));

        using var env = EnvScope.Set(
            (RealFixturesEnv, fixtureDir),
            (PackedFixturesEnv, packedDir),
            (FixtureInventoryEnv, inventoryPath),
            (ReportDirEnv, reportDir));

        UpxPackedFixtures_FindUnpacker_WhenDirectoryConfigured_WritesReport();

        RealFixtureReport[] reports = ReadReports(Path.Combine(reportDir, "upxsharp-upx-packed-fixtures-report.json"));
        Assert.Contains(reports, report =>
            report.Status == RealFixtureStatus.Failed &&
            report.Error?.StartsWith("Official UPX restored bytes differ from inventory original:", StringComparison.Ordinal) == true);
    }

    private static IEnumerable<RealFixtureReport> RunOriginalFixtureReport(string fixtureDir, FixtureInventory? inventory)
    {
        var options = new Options { Method = CompressionMethod.Lzma, Level = 9 };
        if (inventory is null)
        {
            foreach (string fixture in EnumerateFixtures(fixtureDir))
                yield return ProbeOriginalFixture(fixture, expectedFormat: null, options);
        }
        else
        {
            foreach (FixtureInventoryEntry entry in inventory.Fixtures)
                yield return ProbeOriginalFixture(ResolveFixturePath(fixtureDir, entry.Path), entry.Format, options);
        }
    }

    private static RealFixtureReport ProbeOriginalFixture(string fixture, string? expectedFormat, Options options)
    {
        long size = File.Exists(fixture) ? new FileInfo(fixture).Length : 0;
        using var packed = TempFile.CreateEmpty();
        using var unpacked = TempFile.CreateEmpty();

        try
        {
            if (!File.Exists(fixture))
                return new RealFixtureReport(fixture, size, expectedFormat, null, RealFixtureStatus.Missing, "Fixture file not found");

            string format;
            using (var input = new InputFile(fixture))
            using (var output = new OutputFile(packed.Path, overwrite: true))
            {
                var packer = PackMaster.FindPacker(input, options);
                format = packer.GetFormat().ToString();
                packer.DoPack(output, options);
            }

            if (expectedFormat is not null && !string.Equals(format, expectedFormat, StringComparison.Ordinal))
                return new RealFixtureReport(fixture, size, expectedFormat, format, RealFixtureStatus.Failed, $"Expected {expectedFormat}, detected {format}");

            using (var input = new InputFile(packed.Path))
            using (var output = new OutputFile(unpacked.Path, overwrite: true))
            {
                var unpacker = PackMaster.FindUnpacker(input, options);
                unpacker.DoUnpack(output, options);
            }

            return File.ReadAllBytes(fixture).SequenceEqual(File.ReadAllBytes(unpacked.Path))
                ? new RealFixtureReport(fixture, size, expectedFormat, format, RealFixtureStatus.PackedRoundTripOk, null)
                : new RealFixtureReport(fixture, size, expectedFormat, format, RealFixtureStatus.Failed, "Restored bytes differ from original");
        }
        catch (CantPackException ex)
        {
            return new RealFixtureReport(fixture, size, expectedFormat, null, RealFixtureStatus.NotRecognized, ex.Message);
        }
        catch (NotPackedException ex)
        {
            return new RealFixtureReport(fixture, size, expectedFormat, null, RealFixtureStatus.NotPacked, ex.Message);
        }
        catch (CorruptedPackException ex)
        {
            return new RealFixtureReport(fixture, size, expectedFormat, null, RealFixtureStatus.Corrupted, ex.Message);
        }
        catch (OfficialUpxUnsupportedException ex)
        {
            return new RealFixtureReport(fixture, size, expectedFormat, null, RealFixtureStatus.OfficialUpxUnsupported, ex.Message);
        }
        catch (OfficialUpxUnsupportedLayoutException ex)
        {
            return new RealFixtureReport(fixture, size, expectedFormat, null, RealFixtureStatus.OfficialElfUnsupportedLayout, ex.Message);
        }
        catch (OfficialUpxMetadataException ex)
        {
            return new RealFixtureReport(fixture, size, expectedFormat, null, RealFixtureStatus.OfficialUpxMetadataError, ex.Message);
        }
        catch (OfficialUpxDecompressException ex)
        {
            return new RealFixtureReport(fixture, size, expectedFormat, null, RealFixtureStatus.OfficialUpxDecompressError, ex.Message);
        }
        catch (NotImplementedException ex)
        {
            return new RealFixtureReport(fixture, size, expectedFormat, null, RealFixtureStatus.Unsupported, ex.Message);
        }
        catch (Exception ex)
        {
            return new RealFixtureReport(fixture, size, expectedFormat, null, RealFixtureStatus.Failed, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static IEnumerable<RealFixtureReport> RunPackedFixtureReport(string fixtureDir, FixtureInventory? inventory)
    {
        var options = new Options { Method = CompressionMethod.Lzma, Level = 9 };
        if (inventory is null)
        {
            foreach (string fixture in EnumerateFixtures(fixtureDir))
                yield return ProbePackedFixture(fixture, expectedOriginal: null, expectedFormat: null, options);
        }
        else
        {
            string? originalRoot = Environment.GetEnvironmentVariable(RealFixturesEnv);
            foreach (FixtureInventoryEntry entry in inventory.Fixtures)
            {
                string path = entry.PackedPath ?? entry.Path;
                string? expectedOriginal = IsUsableDirectory(originalRoot)
                    ? ResolveFixturePath(originalRoot!, entry.Path)
                    : null;
                yield return ProbePackedFixture(
                    ResolveFixturePath(fixtureDir, path),
                    expectedOriginal,
                    entry.Format,
                    options);
            }
        }
    }

    private static RealFixtureReport ProbePackedFixture(
        string fixture,
        string? expectedOriginal,
        string? expectedFormat,
        Options options)
    {
        long size = File.Exists(fixture) ? new FileInfo(fixture).Length : 0;
        using var unpacked = TempFile.CreateEmpty();

        try
        {
            if (!File.Exists(fixture))
                return new RealFixtureReport(fixture, size, expectedFormat, null, RealFixtureStatus.Missing, "Fixture file not found");

            string format;
            byte[]? directUnpacked = null;
            byte[] packedBytes = File.ReadAllBytes(fixture);
            if (expectedFormat is not null
                && Enum.TryParse(expectedFormat, out UpxFormat expectedUpxFormat)
                && OfficialUpxDetector.TryDetect(packedBytes, expectedUpxFormat, out _))
            {
                directUnpacked = OfficialUpxUnpacker.Unpack(packedBytes, expectedUpxFormat);
                format = expectedUpxFormat.ToString();
            }
            else
            {
                using var input = new InputFile(fixture);
                using var output = new OutputFile(unpacked.Path, overwrite: true);
                var unpacker = PackMaster.FindUnpacker(input, options);
                format = unpacker.GetFormat().ToString();
                unpacker.DoUnpack(output, options);
            }

            return expectedFormat is not null && !string.Equals(format, expectedFormat, StringComparison.Ordinal)
                ? new RealFixtureReport(fixture, size, expectedFormat, format, RealFixtureStatus.Failed, $"Expected {expectedFormat}, detected {format}")
                : expectedOriginal is not null && !File.Exists(expectedOriginal)
                    ? new RealFixtureReport(fixture, size, expectedFormat, format, RealFixtureStatus.Missing, $"Original fixture file not found: {expectedOriginal}")
                : expectedOriginal is not null
                    ? CompareOfficialResult(
                        fixture,
                        size,
                        expectedOriginal,
                        directUnpacked ?? File.ReadAllBytes(unpacked.Path),
                        expectedFormat,
                        format)
                    : new RealFixtureReport(fixture, size, expectedFormat, format, RealFixtureStatus.UpxPackedUnpackOk, null);
        }
        catch (NotPackedException ex)
        {
            return new RealFixtureReport(fixture, size, expectedFormat, null, RealFixtureStatus.NotPacked, ex.Message);
        }
        catch (CantPackException ex)
        {
            return new RealFixtureReport(fixture, size, expectedFormat, null, RealFixtureStatus.NotRecognized, ex.Message);
        }
        catch (CorruptedPackException ex)
        {
            return new RealFixtureReport(fixture, size, expectedFormat, null, RealFixtureStatus.Corrupted, ex.Message);
        }
        catch (OfficialUpxUnsupportedException ex)
        {
            return new RealFixtureReport(fixture, size, expectedFormat, null, RealFixtureStatus.OfficialUpxUnsupported, ex.Message);
        }
        catch (OfficialUpxUnsupportedLayoutException ex)
        {
            return new RealFixtureReport(fixture, size, expectedFormat, null, RealFixtureStatus.OfficialElfUnsupportedLayout, ex.Message);
        }
        catch (OfficialUpxMetadataException ex)
        {
            return new RealFixtureReport(fixture, size, expectedFormat, null, RealFixtureStatus.OfficialUpxMetadataError, ex.Message);
        }
        catch (OfficialUpxDecompressException ex)
        {
            return new RealFixtureReport(fixture, size, expectedFormat, null, RealFixtureStatus.OfficialUpxDecompressError, ex.Message);
        }
        catch (NotImplementedException ex)
        {
            return new RealFixtureReport(fixture, size, expectedFormat, null, RealFixtureStatus.Unsupported, ex.Message);
        }
        catch (Exception ex)
        {
            return new RealFixtureReport(fixture, size, expectedFormat, null, RealFixtureStatus.Failed, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static RealFixtureReport CompareOfficialResult(
        string fixture,
        long packedSize,
        string expectedOriginal,
        byte[] actual,
        string? expectedFormat,
        string detectedFormat)
    {
        byte[] expected = File.ReadAllBytes(expectedOriginal);
        int commonLength = Math.Min(expected.Length, actual.Length);
        int firstDifference = -1;
        for (int index = 0; index < commonLength; index++)
        {
            if (expected[index] != actual[index])
            {
                firstDifference = index;
                break;
            }
        }

        if (firstDifference < 0 && expected.Length == actual.Length)
            return new RealFixtureReport(
                fixture,
                packedSize,
                expectedFormat,
                detectedFormat,
                RealFixtureStatus.UpxPackedUnpackOk,
                null);

        if (firstDifference < 0)
            firstDifference = commonLength;
        return new RealFixtureReport(
            fixture,
            packedSize,
            expectedFormat,
            detectedFormat,
            RealFixtureStatus.Failed,
            $"Official UPX restored bytes differ from inventory original: expectedLength={expected.Length}, actualLength={actual.Length}, firstDifference=0x{firstDifference:X}");
    }

    private static void WriteReport(string fileName, IReadOnlyCollection<RealFixtureReport> reports)
    {
        string reportDir = Environment.GetEnvironmentVariable(ReportDirEnv) ?? Path.GetTempPath();
        Directory.CreateDirectory(reportDir);
        string reportPath = Path.Combine(reportDir, fileName);
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        File.WriteAllText(reportPath, JsonSerializer.Serialize(reports, options));
    }

    private static void WriteFormatReport(string fileName, IReadOnlyCollection<FixtureFormatReport> reports)
    {
        string reportDir = Environment.GetEnvironmentVariable(ReportDirEnv) ?? Path.GetTempPath();
        Directory.CreateDirectory(reportDir);
        string reportPath = Path.Combine(reportDir, fileName);
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        File.WriteAllText(reportPath, JsonSerializer.Serialize(reports, options));
    }

    private static RealFixtureStatus GetOfficialFailureStatus(UpxException exception)
        => exception switch
        {
            OfficialUpxUnsupportedLayoutException => RealFixtureStatus.OfficialElfUnsupportedLayout,
            OfficialUpxMetadataException => RealFixtureStatus.OfficialUpxMetadataError,
            OfficialUpxDecompressException => RealFixtureStatus.OfficialUpxDecompressError,
            OfficialUpxUnsupportedException => RealFixtureStatus.OfficialUpxUnsupported,
            _ => RealFixtureStatus.Failed,
        };

    private static void EnforceStrict(
        FixtureInventory? inventory,
        IReadOnlyCollection<RealFixtureReport>? originalReports,
        IReadOnlyCollection<RealFixtureReport>? packedReports)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(StrictEnv), "1", StringComparison.Ordinal))
            return;

        Assert.NotNull(inventory);
        FixtureFormatReport[] formatReports = BuildFormatReports(inventory, originalReports, packedReports).ToArray();
        var failures = formatReports.Where(r => !r.Covered ||
            !r.Recognized ||
            (originalReports is not null && !r.RoundTripOk) ||
            (packedReports is not null && !r.UpxPackedUnpackOk)).ToList();

        Assert.True(failures.Count == 0, "Strict real fixture failures: " +
            string.Join("; ", failures.Select(f => $"{f.Format}: {f.Status} {f.Error}")));
    }

    private static bool IsUsableDirectory(string? path)
        => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);

    private static IEnumerable<string> EnumerateFixtures(string directory)
    {
        static bool IsAllowed(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            return extension is "" or ".exe" or ".elf" or ".bin" or ".upx";
        }

        return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(IsAllowed)
            .Where(path => new FileInfo(path).Length is > 0 and <= MaxFixtureSize)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    private static RealFixtureReport[] ReadReports(string path)
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter());
        return JsonSerializer.Deserialize<RealFixtureReport[]>(File.ReadAllText(path), options) ?? [];
    }

    private static FixtureFormatReport[] ReadFormatReports(string path)
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter());
        return JsonSerializer.Deserialize<FixtureFormatReport[]>(File.ReadAllText(path), options) ?? [];
    }

    private static FixtureInventory? LoadInventoryOrNull()
    {
        string? path = Environment.GetEnvironmentVariable(FixtureInventoryEnv);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<FixtureInventory>(File.ReadAllText(path), options)
            ?? throw new InvalidOperationException($"Fixture inventory is invalid: {path}");
    }

    private static IEnumerable<FixtureFormatReport> BuildFormatReports(
        FixtureInventory? inventory,
        IReadOnlyCollection<RealFixtureReport>? originalReports,
        IReadOnlyCollection<RealFixtureReport>? packedReports)
    {
        string[] expectedFormats = ExpectedFormats();
        foreach (string format in expectedFormats)
        {
            RealFixtureReport? original = originalReports?.FirstOrDefault(r => string.Equals(r.ExpectedFormat, format, StringComparison.Ordinal));
            RealFixtureReport? packed = packedReports?.FirstOrDefault(r => string.Equals(r.ExpectedFormat, format, StringComparison.Ordinal));
            bool declared = inventory?.Fixtures.Any(f => string.Equals(f.Format, format, StringComparison.Ordinal)) ?? false;
            RealFixtureReport? report = original ?? packed;
            var status = !declared ? FixtureFormatStatus.Missing :
                report is null ? FixtureFormatStatus.Missing :
                report.Status is RealFixtureStatus.PackedRoundTripOk or RealFixtureStatus.UpxPackedUnpackOk ? FixtureFormatStatus.Covered :
                FixtureFormatStatus.Failed;

            yield return new FixtureFormatReport(
                format,
                declared,
                (original ?? packed)?.DetectedFormat is not null,
                original?.Status == RealFixtureStatus.PackedRoundTripOk,
                packed?.Status == RealFixtureStatus.UpxPackedUnpackOk,
                status,
                report?.File,
                report?.Error);
        }
    }

    private static string ResolveFixturePath(string root, string relativePath)
        => Path.IsPathRooted(relativePath) ? relativePath : Path.Combine(root, relativePath);

    private static string[] ExpectedFormats()
        => Enum.GetNames<UpxFormat>()
            .Where(name => name is not nameof(UpxFormat.DosSys) and not nameof(UpxFormat.Djgpp2Coff))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    private static void WriteCompleteSyntheticInventory(string fixtureDir, string inventoryPath, bool includePackedPaths)
    {
        var fixtures = new List<FixtureInventoryEntry>();
        foreach ((string format, byte[] bytes) in SyntheticFixturesByFormat())
        {
            string fileName = format + ".bin";
            File.WriteAllBytes(Path.Combine(fixtureDir, fileName), bytes);
            fixtures.Add(new FixtureInventoryEntry(format, fileName, includePackedPaths ? fileName + ".upx" : null));
        }

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(inventoryPath, JsonSerializer.Serialize(new FixtureInventory(fixtures.ToArray()), options));
    }

    private static byte[] PackSynthetic(byte[] original)
    {
        var options = new Options { Method = CompressionMethod.Lzma, Level = 9 };
        using var input = TempFile.Create(original);
        using var packed = TempFile.CreateEmpty();

        using (var inputFile = new InputFile(input.Path))
        using (var outputFile = new OutputFile(packed.Path, overwrite: true))
            PackMaster.FindPacker(inputFile, options).DoPack(outputFile, options);

        return File.ReadAllBytes(packed.Path);
    }

    private static IEnumerable<(string Format, byte[] Bytes)> SyntheticFixturesByFormat()
    {
        foreach (object[] fixture in IntegrationTests.ImplementedFormatFixtures())
        {
            string name = (string)fixture[0];
            byte[] bytes = (byte[])fixture[1];
            yield return (name switch
            {
                "pe32" => nameof(UpxFormat.Win32Pe),
                "pe64" => nameof(UpxFormat.Win64Pe),
                "elf-i386" => nameof(UpxFormat.LinuxElfi386),
                "elf-amd64" => nameof(UpxFormat.LinuxElfAmd64),
                "elf-arm32" => nameof(UpxFormat.LinuxElfArmLE),
                "elf-arm64" => nameof(UpxFormat.LinuxElfArm64LE),
                "elf-mips" => nameof(UpxFormat.LinuxElfMipsel),
                "elf-mips-be" => nameof(UpxFormat.LinuxElfMips),
                "elf-ppc32" => nameof(UpxFormat.LinuxElf32Ppc),
                "elf-ppc64" => nameof(UpxFormat.LinuxElf64Ppc),
                "elf-riscv64" => nameof(UpxFormat.LinuxElfRiscv64),
                "mach-i386" => nameof(UpxFormat.MachI386),
                "mach-amd64" => nameof(UpxFormat.MachAMD64),
                "vmlinuz-i386" => nameof(UpxFormat.Vmlinuzi386),
                "dos-exe" => nameof(UpxFormat.DosExe),
                "dos-com" => nameof(UpxFormat.DosCom),
                "atari-tos" => nameof(UpxFormat.AtariTos),
                "ps1-exe" => nameof(UpxFormat.Ps1Exe),
                _ => throw new InvalidOperationException($"Unknown synthetic fixture: {name}"),
            }, bytes);
        }
    }

    private static byte[] GetSyntheticFixture()
        => (byte[])IntegrationTests.ImplementedFormatFixtures().First()[1];

    public sealed record RealFixtureReport(
        string File,
        long Size,
        string? ExpectedFormat,
        string? DetectedFormat,
        RealFixtureStatus Status,
        string? Error);

    public sealed record FixtureFormatReport(
        string Format,
        bool Covered,
        bool Recognized,
        bool RoundTripOk,
        bool UpxPackedUnpackOk,
        FixtureFormatStatus Status,
        string? File,
        string? Error);

    public sealed record FixtureInventory(
        [property: JsonPropertyName("fixtures")] FixtureInventoryEntry[] Fixtures);

    public sealed record FixtureInventoryEntry(
        [property: JsonPropertyName("format")] string Format,
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("packedPath")] string? PackedPath);

    public enum RealFixtureStatus
    {
        PackedRoundTripOk,
        UpxPackedUnpackOk,
        Missing,
        NotRecognized,
        NotPacked,
        Corrupted,
        OfficialUpxUnsupported,
        OfficialElfUnsupportedLayout,
        OfficialUpxMetadataError,
        OfficialUpxDecompressError,
        Unsupported,
        Failed,
    }

    public enum FixtureFormatStatus
    {
        Covered,
        Missing,
        Failed,
    }

    private sealed class EnvScope : IDisposable
    {
        private readonly Dictionary<string, string?> _previous = [];

        private EnvScope((string Name, string? Value)[] values)
        {
            foreach ((string name, string? value) in values)
            {
                _previous[name] = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        public static EnvScope Set(params (string Name, string? Value)[] values)
            => new(values);

        public void Dispose()
        {
            foreach ((string name, string? value) in _previous)
                Environment.SetEnvironmentVariable(name, value);
        }
    }

    private sealed class TempFile : IDisposable
    {
        private TempFile(string path) => Path = path;

        public string Path { get; }

        public static TempFile CreateEmpty()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"upxsharp-real-fixture-{Guid.NewGuid():N}.bin");
            return new TempFile(path);
        }

        public static TempFile Create(byte[] bytes)
        {
            var file = CreateEmpty();
            File.WriteAllBytes(file.Path, bytes);
            return file;
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
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "upxsharp-real-fixtures-" + Guid.NewGuid().ToString("N"));
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
