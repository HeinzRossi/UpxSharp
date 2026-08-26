using CommunityToolkit.Mvvm.ComponentModel;

namespace UpxSharp.Wpf.ViewModels;

public partial class FileItemViewModel : ObservableObject
{
    [ObservableProperty] private string _filePath = string.Empty;
    [ObservableProperty] private string _fileName = string.Empty;
    [ObservableProperty] private long _originalSize;
    [ObservableProperty] private long _compressedSize;
    [ObservableProperty] private double _ratio;
    [ObservableProperty] private FileProcessingStatus _status = FileProcessingStatus.Pending;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private double _progress;

    public string OriginalSizeFormatted => FormatSize(OriginalSize);
    public string CompressedSizeFormatted => FormatSize(CompressedSize);
    public string RatioFormatted => Ratio > 0 ? $"{Ratio * 100:F1}% menor" : "—";

    partial void OnOriginalSizeChanged(long value)
        => OnPropertyChanged(nameof(OriginalSizeFormatted));

    partial void OnCompressedSizeChanged(long value)
    {
        OnPropertyChanged(nameof(CompressedSizeFormatted));
        if (OriginalSize > 0)
            Ratio = 1.0 - (double)CompressedSize / OriginalSize;
    }

    partial void OnRatioChanged(double value)
        => OnPropertyChanged(nameof(RatioFormatted));

    private static string FormatSize(long bytes) => bytes switch
    {
        <= 0 => "—",
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
    };
}

public enum FileProcessingStatus { Pending, Processing, Done, Error, Skipped }
