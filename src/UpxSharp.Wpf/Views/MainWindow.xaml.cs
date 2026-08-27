using System.ComponentModel;
using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Input;
using UpxSharp.Wpf.Services;
using UpxSharp.Wpf.ViewModels;

namespace UpxSharp.Wpf.Views;

public partial class MainWindow : Window
{
    private readonly IWindowSettingsService _windowSettingsService;

    public MainWindow(IWindowSettingsService windowSettingsService)
    {
        _windowSettingsService = windowSettingsService;
        InitializeComponent();
        RestoreWindowSettings();
        Closing += OnClosing;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (IsProcessElevated() && DataContext is MainViewModel viewModel)
        {
            viewModel.ProgressViewModel.AddLog(
                "Drag and drop pode ser bloqueado quando o UpxSharp roda como administrador.",
                LogLevel.Warning);
        }
    }

    private void RestoreWindowSettings()
    {
        WindowSettings? settings = _windowSettingsService.Load();
        if (settings is null)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        ApplyWindowSettings(settings);
    }

    private void ApplyWindowSettings(WindowSettings settings)
    {
        Rect workArea = SystemParameters.WorkArea;
        var virtualScreen = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        WindowSettings normalized = WindowSettingsService.Normalize(
            settings,
            workArea,
            virtualScreen,
            MinWidth,
            MinHeight);

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = normalized.Left;
        Top = normalized.Top;
        Width = normalized.Width;
        Height = normalized.Height;

        if (normalized.IsMaximized)
            WindowState = WindowState.Maximized;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        Rect bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;

        try
        {
            _windowSettingsService.Save(new WindowSettings(
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height,
                WindowState == WindowState.Maximized));
        }
        catch (IOException)
        {
            // Falha de persistência não deve impedir o fechamento.
        }
        catch (UnauthorizedAccessException)
        {
            // Falha de persistência não deve impedir o fechamento.
        }
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        else
            DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => Close();

    private void Window_PreviewDragEnter(object sender, DragEventArgs e)
        => HandleDrag(e, updateHover: true);

    private void Window_PreviewDragOver(object sender, DragEventArgs e)
        => HandleDrag(e, updateHover: true);

    private void Window_PreviewDragLeave(object sender, DragEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
            viewModel.IsDragOver = false;

        e.Handled = true;
    }

    private void Window_PreviewDrop(object sender, DragEventArgs e)
    {
        string[] files = DragDropFileHelper.GetExistingFiles(e.Data);
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.IsDragOver = false;
            if (files.Length > 0 && viewModel.FilesDroppedCommand.CanExecute(files))
                viewModel.FilesDroppedCommand.Execute(files);
        }

        e.Effects = files.Length > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void HandleDrag(DragEventArgs e, bool updateHover)
    {
        string[] files = DragDropFileHelper.GetExistingFiles(e.Data);
        bool hasFiles = files.Length > 0;
        e.Effects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;

        if (updateHover && DataContext is MainViewModel viewModel)
            viewModel.IsDragOver = hasFiles;

        e.Handled = true;
    }

    private static bool IsProcessElevated()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
