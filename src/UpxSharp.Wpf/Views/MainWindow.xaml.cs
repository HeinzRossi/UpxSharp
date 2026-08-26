using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using UpxSharp.Wpf.Services;

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
}
