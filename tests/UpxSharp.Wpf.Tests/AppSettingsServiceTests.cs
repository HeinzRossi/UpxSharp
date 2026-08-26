using UpxSharp.Wpf.Services;
using Xunit;

namespace UpxSharp.Wpf.Tests;

public sealed class AppSettingsServiceTests
{
    [Fact]
    public void Load_WhenFileMissing_ReturnsDefault()
    {
        using var directory = new TempDirectory();
        var service = new AppSettingsService(Path.Combine(directory.Path, "settings.json"));

        AppSettings settings = service.Load();

        Assert.False(settings.CloseAfterContextMenuExecution);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsCloseAfterContextMenuExecution()
    {
        using var directory = new TempDirectory();
        var service = new AppSettingsService(Path.Combine(directory.Path, "settings.json"));

        service.Save(new AppSettings(true));

        Assert.True(service.Load().CloseAfterContextMenuExecution);
    }

    [Fact]
    public void Load_WhenJsonInvalid_ReturnsDefault()
    {
        using var directory = new TempDirectory();
        string path = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(path, "{not-json");
        var service = new AppSettingsService(path);

        Assert.False(service.Load().CloseAfterContextMenuExecution);
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"upxsharp-settings-tests-{Guid.NewGuid():N}");

        public TempDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
