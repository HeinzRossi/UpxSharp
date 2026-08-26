using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Windows;
using UpxSharp.Wpf.Services;
using UpxSharp.Wpf.ViewModels;
using UpxSharp.Wpf.Views;

namespace UpxSharp.Wpf;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static IServiceProvider services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var svc = new ServiceCollection();

        svc.AddSingleton<IFileProcessingService, FileProcessingService>();
        svc.AddSingleton<IDialogService, DialogService>();
        svc.AddSingleton<IWindowSettingsService, WindowSettingsService>();
        svc.AddSingleton<IExplorerContextMenuService, ExplorerContextMenuService>();
        svc.AddSingleton<IAppSettingsService, AppSettingsService>();
        svc.AddSingleton<OptionsViewModel>();
        svc.AddSingleton<ProgressViewModel>();
        svc.AddSingleton<MainViewModel>();
        svc.AddSingleton<MainWindow>();

        services = svc.BuildServiceProvider();

        var win = services.GetRequiredService<MainWindow>();
        var viewModel = services.GetRequiredService<MainViewModel>();
        win.DataContext = viewModel;
        win.Show();

        if (TryGetContextMenuFile(e.Args, out string? contextMenuFile))
            _ = Dispatcher.BeginInvoke(async () =>
                await RunContextMenuCompressionAsync(win, viewModel, contextMenuFile!));
    }

    private static bool TryGetContextMenuFile(string[] args, out string? filePath)
    {
        filePath = null;
        if (args.Length != 2 || !string.Equals(args[0], "--context-menu", StringComparison.OrdinalIgnoreCase))
            return false;

        string candidate = args[1];
        if (!string.Equals(Path.GetExtension(candidate), ".exe", StringComparison.OrdinalIgnoreCase))
            return false;

        if (IsSelfExecutable(candidate))
            return false;

        filePath = candidate;
        return true;
    }

    private static async Task RunContextMenuCompressionAsync(
        Window owner,
        MainViewModel viewModel,
        string filePath)
    {
        viewModel.AddFiles([filePath]);

        if (!File.Exists(filePath) || !viewModel.HasFiles)
        {
            MessageBox.Show(
                owner,
                "Arquivo executável não encontrado.",
                "UpxSharp",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        bool succeeded = await viewModel.CompressForContextMenuAsync(CancellationToken.None);
        if (succeeded && viewModel.OptionsViewModel.CloseAfterContextMenuExecution)
            owner.Close();
    }

    private static bool IsSelfExecutable(string filePath)
    {
        try
        {
            string fullPath = Path.GetFullPath(filePath);
            string? currentPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(currentPath))
                return false;

            return string.Equals(
                fullPath,
                Path.GetFullPath(currentPath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (PathTooLongException)
        {
            return false;
        }
    }
}
