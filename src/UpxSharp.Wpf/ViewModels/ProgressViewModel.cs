using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace UpxSharp.Wpf.ViewModels;

public partial class ProgressViewModel : ObservableObject
{
    public ObservableCollection<LogEntry> LogEntries { get; } = [];
    public bool HasLogs => LogEntries.Count > 0;
    public bool IsPanelVisible => IsProcessing || HasLogs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPanelVisible))]
    private bool _isProcessing;
    [ObservableProperty] private string _currentFileName = string.Empty;
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private string _speed = string.Empty;

    public ProgressViewModel()
    {
        LogEntries.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasLogs));
            OnPropertyChanged(nameof(IsPanelVisible));
        };
    }

    public void UpdateCurrentFile(string name, double percent)
    {
        RunOnUiThread(() =>
        {
            CurrentFileName = name;
            ProgressPercent = percent;
        });
    }

    public void AddLog(string message, LogLevel level)
        => RunOnUiThread(() => LogEntries.Add(new LogEntry(message, level)));

    public void Clear()
    {
        RunOnUiThread(() =>
        {
            LogEntries.Clear();
            ProgressPercent = 0;
            CurrentFileName = string.Empty;
            Speed = string.Empty;
        });
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }
}

public record LogEntry(string Message, LogLevel Level);
public enum LogLevel { Info, Success, Warning, Error }
