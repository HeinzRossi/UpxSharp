using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UpxSharp.Wpf.ViewModels;

namespace UpxSharp.Wpf.Views.Controls;

public partial class ProgressPanel : UserControl
{
    public ProgressPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => ScrollToEnd();
    }

    private void OnDataContextChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ProgressViewModel oldViewModel)
            oldViewModel.LogEntries.CollectionChanged -= LogEntriesChanged;

        if (e.NewValue is ProgressViewModel newViewModel)
            newViewModel.LogEntries.CollectionChanged += LogEntriesChanged;
    }

    private void LogEntriesChanged(
        object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
        {
            Dispatcher.BeginInvoke(
                ScrollToEnd,
                System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private void ScrollToEnd() => LogScrollViewer?.ScrollToEnd();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (Keyboard.Modifiers != ModifierKeys.Control)
            return;

        if (e.Key == Key.C)
        {
            CopyLogToClipboard();
            e.Handled = true;
        }
        else if (e.Key == Key.L)
        {
            if (DataContext is ProgressViewModel viewModel)
                viewModel.Clear();
            e.Handled = true;
        }
    }

    private void CopyLogToClipboard()
    {
        if (DataContext is not ProgressViewModel viewModel ||
            viewModel.LogEntries.Count == 0)
        {
            return;
        }

        Clipboard.SetText(string.Join(
            Environment.NewLine,
            viewModel.LogEntries.Select(entry => entry.Message)));
    }

    private void MenuItemCopyLog_Click(object sender, RoutedEventArgs e)
        => CopyLogToClipboard();

    private void MenuItemClearLog_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ProgressViewModel viewModel)
            viewModel.Clear();
    }
}
