using UpxSharp.Core;
using UpxSharp.Wpf.ViewModels;

namespace UpxSharp.Wpf.Services;

public enum FileOutputMode
{
    ReplaceOriginal,
    SameDirectoryWithSuffix,
    CustomDirectory,
}

public sealed record FileProcessingRequest(
    Options Options,
    FileOutputMode OutputMode,
    string? CustomOutputDirectory);

public interface IFileProcessingService
{
    Task ProcessAsync(
        IEnumerable<FileItemViewModel> files,
        FileProcessingRequest request,
        Action<FileItemViewModel, double> onProgress,
        Action<string, LogLevel> onLog,
        CancellationToken cancellationToken);
}
