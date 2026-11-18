using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace UpxSharp.Wpf.ViewModels;

public partial class ProgressViewModel : ObservableObject
{
    private const int ProgressAnimationDurationMilliseconds = 250;
    private const int ProgressAnimationStepMilliseconds = 25;
    private readonly object _currentFileProgressLock = new();
    private readonly object _overallProgressLock = new();
    private CancellationTokenSource _currentFileProgressAnimation = new();
    private CancellationTokenSource _overallProgressAnimation = new();

    public ObservableCollection<LogEntry> LogEntries { get; } = [];
    public bool HasLogs => LogEntries.Count > 0;
    public bool IsPanelVisible => IsProcessing || HasLogs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPanelVisible))]
    private bool _isProcessing;
    [ObservableProperty] private string _currentFileName = string.Empty;
    [ObservableProperty] private double _currentFileProgressPercent;
    [ObservableProperty] private double _overallProgressPercent;
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
        RunOnUiThread(() => CurrentFileName = name);
        AnimateCurrentFileProgress(ClampProgress(percent));
    }

    public void UpdateOverallProgress(double percent)
        => AnimateOverallProgress(ClampProgress(percent));

    public void AddLog(string message, LogLevel level)
        => RunOnUiThread(() => LogEntries.Add(new LogEntry(message, level)));

    public void Clear()
    {
        CancelProgressAnimations();
        RunOnUiThread(() =>
        {
            LogEntries.Clear();
            CurrentFileProgressPercent = 0;
            OverallProgressPercent = 0;
            CurrentFileName = string.Empty;
            Speed = string.Empty;
        });
    }

    private void AnimateCurrentFileProgress(double target)
    {
        CancellationToken token;
        double start = CurrentFileProgressPercent;
        lock (_currentFileProgressLock)
        {
            _currentFileProgressAnimation.Cancel();
            _currentFileProgressAnimation = new CancellationTokenSource();
            token = _currentFileProgressAnimation.Token;
        }

        SetProgress(start, target, value => CurrentFileProgressPercent = value, token);
    }

    private void AnimateOverallProgress(double target)
    {
        CancellationToken token;
        double start = OverallProgressPercent;
        lock (_overallProgressLock)
        {
            _overallProgressAnimation.Cancel();
            _overallProgressAnimation = new CancellationTokenSource();
            token = _overallProgressAnimation.Token;
        }

        SetProgress(start, target, value => OverallProgressPercent = value, token);
    }

    private static void SetProgress(
        double start,
        double target,
        Action<double> setProgress,
        CancellationToken cancellationToken)
    {
        if (target <= 0 || target < start || Math.Abs(target - start) < 1)
        {
            RunOnUiThread(() => setProgress(target));
            return;
        }

        _ = AnimateProgressAsync(start, target, setProgress, cancellationToken);
    }

    private static async Task AnimateProgressAsync(
        double start,
        double target,
        Action<double> setProgress,
        CancellationToken cancellationToken)
    {
        int steps = Math.Max(1, ProgressAnimationDurationMilliseconds / ProgressAnimationStepMilliseconds);

        try
        {
            for (int step = 1; step <= steps; step++)
            {
                await Task.Delay(ProgressAnimationStepMilliseconds, cancellationToken);
                double progress = start + ((target - start) * step / steps);
                RunOnUiThread(() => setProgress(progress));
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void CancelProgressAnimations()
    {
        lock (_currentFileProgressLock)
            _currentFileProgressAnimation.Cancel();
        lock (_overallProgressLock)
            _overallProgressAnimation.Cancel();
    }

    private static double ClampProgress(double percent)
        => Math.Clamp(percent, 0, 100);

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
