using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using UpxSharp.Core;
using UpxSharp.Wpf.Services;

namespace UpxSharp.Wpf.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IFileProcessingService _processingService;
    private readonly IDialogService _dialogService;

    public OptionsViewModel OptionsViewModel { get; }
    public ProgressViewModel ProgressViewModel { get; }
    public ObservableCollection<FileItemViewModel> Files { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProcessing))]
    private AppState _state = AppState.Idle;

    [ObservableProperty]
    private bool _isDragOver;

    public bool HasFiles => Files.Count > 0;
    public bool IsProcessing => State == AppState.Processing;

    public MainViewModel(
        IFileProcessingService processingService,
        IDialogService dialogService,
        OptionsViewModel optionsViewModel,
        ProgressViewModel progressViewModel)
    {
        _processingService = processingService;
        _dialogService = dialogService;
        OptionsViewModel = optionsViewModel;
        ProgressViewModel = progressViewModel;

        Files.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasFiles));
            NotifyProcessCommandsCanExecuteChanged();
        };
    }

    [RelayCommand]
    private void FilesDropped(string[] paths)
        => AddFiles(paths);

    public void AddFiles(IEnumerable<string> paths)
    {
        foreach (string path in paths.Where(File.Exists))
        {
            string fullPath = Path.GetFullPath(path);
            if (Files.Any(file => string.Equals(
                file.FilePath, fullPath, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var info = new FileInfo(fullPath);
            Files.Add(new FileItemViewModel
            {
                FilePath = fullPath,
                FileName = info.Name,
                OriginalSize = info.Length,
                Status = FileProcessingStatus.Pending,
            });
        }

        IsDragOver = false;
        State = HasFiles ? AppState.FilesLoaded : AppState.Idle;
    }

    [RelayCommand]
    private void BrowseFiles()
    {
        var paths = _dialogService.OpenFiles(
            "Selecionar executáveis",
            "Executáveis|*.exe;*.dll;*.so;*.elf;*.bin|Todos os arquivos|*.*");

        if (paths is { Length: > 0 })
            FilesDropped(paths);
    }

    [RelayCommand(CanExecute = nameof(CanProcess))]
    private Task CompressAsync(CancellationToken cancellationToken)
        => ProcessAsync(UpxCommand.Compress, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanProcess))]
    private Task DecompressAsync(CancellationToken cancellationToken)
        => ProcessAsync(UpxCommand.Decompress, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanProcess))]
    private Task TestAsync(CancellationToken cancellationToken)
        => ProcessAsync(UpxCommand.Test, cancellationToken);

    private bool CanProcess() => HasFiles && !IsProcessing;

    public Task<bool> CompressForContextMenuAsync(CancellationToken cancellationToken)
        => ProcessAsync(UpxCommand.Compress, cancellationToken);

    private async Task<bool> ProcessAsync(UpxCommand command, CancellationToken cancellationToken)
    {
        State = AppState.Processing;
        ProgressViewModel.IsProcessing = true;
        ProgressViewModel.Clear();

        var request = OptionsViewModel.ToProcessingRequest(command);

        try
        {
            await _processingService.ProcessAsync(
                Files,
                request,
                onProgress: (file, progress) =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        file.Progress = progress;
                        ProgressViewModel.UpdateCurrentFile(file.FileName, progress);
                    });
                },
                onOverallProgress: overallProgress =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        ProgressViewModel.UpdateOverallProgress(overallProgress);
                    });
                },
                onLog: ProgressViewModel.AddLog,
                cancellationToken);

            State = Files.Any(file => file.Status == FileProcessingStatus.Error)
                ? AppState.Error
                : AppState.Done;
            return State == AppState.Done;
        }
        catch (OperationCanceledException)
        {
            ProgressViewModel.AddLog("Processamento cancelado.", LogLevel.Warning);
            State = HasFiles ? AppState.FilesLoaded : AppState.Idle;
            return false;
        }
        catch (Exception ex)
        {
            ProgressViewModel.AddLog($"Erro inesperado: {ex.Message}", LogLevel.Error);
            State = AppState.Error;
            return false;
        }
        finally
        {
            ProgressViewModel.IsProcessing = false;
            NotifyProcessCommandsCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanModifyFiles))]
    private void RemoveFile(FileItemViewModel file)
    {
        Files.Remove(file);
        State = HasFiles ? AppState.FilesLoaded : AppState.Idle;
    }

    private bool CanModifyFiles() => !IsProcessing;

    [RelayCommand(CanExecute = nameof(CanModifyFiles))]
    private void ClearFiles()
    {
        Files.Clear();
        ProgressViewModel.Clear();
        State = AppState.Idle;
    }

    [RelayCommand(CanExecute = nameof(CanCancelProcessing))]
    private void CancelProcessing()
    {
        CompressCommand.Cancel();
        DecompressCommand.Cancel();
        TestCommand.Cancel();
    }

    private bool CanCancelProcessing() => IsProcessing;

    partial void OnStateChanged(AppState value)
    {
        NotifyProcessCommandsCanExecuteChanged();
        RemoveFileCommand.NotifyCanExecuteChanged();
        ClearFilesCommand.NotifyCanExecuteChanged();
        CancelProcessingCommand.NotifyCanExecuteChanged();
    }

    private void NotifyProcessCommandsCanExecuteChanged()
    {
        CompressCommand.NotifyCanExecuteChanged();
        DecompressCommand.NotifyCanExecuteChanged();
        TestCommand.NotifyCanExecuteChanged();
    }
}

public enum AppState { Idle, FilesLoaded, Processing, Done, Error }
