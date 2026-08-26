using System.Windows;
using UpxSharp.Wpf.Services;
using Xunit;

namespace UpxSharp.Wpf.Tests;

public sealed class WindowSettingsServiceTests
{
    [Fact]
    public void Load_WhenFileMissing_ReturnsNull()
    {
        using var directory = new TempDirectory();
        var service = new WindowSettingsService(
            Path.Combine(directory.Path, "settings.json"));

        Assert.Null(service.Load());
    }

    [Fact]
    public void SaveThenLoad_RoundTripsSettings()
    {
        using var directory = new TempDirectory();
        var service = new WindowSettingsService(
            Path.Combine(directory.Path, "settings.json"));
        var expected = new WindowSettings(120, 80, 1100, 720, true);

        service.Save(expected);

        Assert.Equal(expected, service.Load());
    }

    [Fact]
    public void Load_WhenJsonInvalid_ReturnsNull()
    {
        using var directory = new TempDirectory();
        string path = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(path, "{not-json");
        var service = new WindowSettingsService(path);

        Assert.Null(service.Load());
    }

    [Fact]
    public void Normalize_WhenOutsideVirtualScreen_CentersInWorkArea()
    {
        var settings = new WindowSettings(9000, 9000, 1000, 680, false);
        var workArea = new Rect(0, 0, 1920, 1040);
        var virtualScreen = new Rect(0, 0, 1920, 1080);

        WindowSettings normalized = WindowSettingsService.Normalize(
            settings,
            workArea,
            virtualScreen);

        Assert.Equal(460, normalized.Left);
        Assert.Equal(180, normalized.Top);
        Assert.Equal(1000, normalized.Width);
        Assert.Equal(680, normalized.Height);
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"upxsharp-window-tests-{Guid.NewGuid():N}");

        public TempDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
