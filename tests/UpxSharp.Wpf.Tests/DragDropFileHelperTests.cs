using System.Windows;
using UpxSharp.Wpf.Services;
using UpxSharp.Wpf.ViewModels;
using Xunit;

namespace UpxSharp.Wpf.Tests;

public sealed class DragDropFileHelperTests
{
    [Fact]
    public void GetExistingFiles_ReturnsOnlyExistingFiles()
    {
        using var directory = new TempDirectory();
        string filePath = directory.CreateFile("sample.exe");
        string childDirectory = Directory.CreateDirectory(Path.Combine(directory.Path, "child")).FullName;
        string missingPath = Path.Combine(directory.Path, "missing.exe");
        var data = new DataObject(DataFormats.FileDrop, new[] { filePath, childDirectory, missingPath });

        string[] files = DragDropFileHelper.GetExistingFiles(data);

        Assert.Equal([Path.GetFullPath(filePath)], files);
        Assert.Equal(DragDropEffects.Copy, DragDropFileHelper.GetDropEffects(data));
    }

    [Fact]
    public void GetDropEffects_WhenNoExistingFiles_ReturnsNone()
    {
        var data = new DataObject(DataFormats.FileDrop, new[] { "C:\\missing\\sample.exe" });

        Assert.Empty(DragDropFileHelper.GetExistingFiles(data));
        Assert.Equal(DragDropEffects.None, DragDropFileHelper.GetDropEffects(data));
    }

    [Fact]
    public void FilesDroppedCommand_AddsFilesAndIgnoresDuplicates()
    {
        using var directory = new TempDirectory();
        string filePath = directory.CreateFile("sample.exe");
        var viewModel = CreateMainViewModel();

        viewModel.FilesDroppedCommand.Execute(new[] { filePath, filePath });

        Assert.Single(viewModel.Files);
        Assert.True(viewModel.HasFiles);
        Assert.Equal(AppState.FilesLoaded, viewModel.State);
        Assert.Equal(Path.GetFullPath(filePath), viewModel.Files[0].FilePath);
    }

    [Fact]
    public void FilesDroppedCommand_WhenFilesMissing_DoesNotChangeList()
    {
        var viewModel = CreateMainViewModel();

        viewModel.FilesDroppedCommand.Execute(new[] { "C:\\missing\\sample.exe" });

        Assert.Empty(viewModel.Files);
        Assert.False(viewModel.HasFiles);
        Assert.Equal(AppState.Idle, viewModel.State);
    }

    private static MainViewModel CreateMainViewModel()
        => new(
            new NoOpFileProcessingService(),
            new NoOpDialogService(),
            new OptionsViewModel(
                new NoOpDialogService(),
                new NoOpExplorerContextMenuService(),
                new InMemoryAppSettingsService()),
            new ProgressViewModel());

    private sealed class NoOpFileProcessingService : IFileProcessingService
    {
        public Task ProcessAsync(
            IEnumerable<FileItemViewModel> files,
            FileProcessingRequest request,
            Action<FileItemViewModel, double> onProgress,
            Action<double> onOverallProgress,
            Action<string, LogLevel> onLog,
            CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class NoOpDialogService : IDialogService
    {
        public string[]? OpenFiles(string title, string filter) => [];
        public string? OpenFolder(string title) => null;
    }

    private sealed class NoOpExplorerContextMenuService : IExplorerContextMenuService
    {
        public string LastInstallMessage => string.Empty;
        public void InstallForCurrentUser(bool autoPack) { }
        public void RemoveForCurrentUser() { }
        public bool IsInstalledForCurrentUser() => false;
        public ExplorerIntegrationStatus GetStatus() => ExplorerIntegrationStatus.NotInstalled;
    }

    private sealed class InMemoryAppSettingsService : IAppSettingsService
    {
        private AppSettings _settings = new(false);

        public AppSettings Load() => _settings;
        public void Save(AppSettings settings) => _settings = settings;
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"upxsharp-dragdrop-tests-{Guid.NewGuid():N}");

        public TempDirectory() => Directory.CreateDirectory(Path);

        public string CreateFile(string name)
        {
            string path = System.IO.Path.Combine(Path, name);
            File.WriteAllText(path, string.Empty);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
