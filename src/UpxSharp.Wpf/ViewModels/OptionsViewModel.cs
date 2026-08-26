using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UpxSharp.Core;
using UpxSharp.Wpf.Services;

namespace UpxSharp.Wpf.ViewModels;

public partial class OptionsViewModel : ObservableObject
{
    private readonly IDialogService _dialogService;
    private readonly IExplorerContextMenuService _explorerContextMenuService;
    private readonly IAppSettingsService _appSettingsService;

    public IReadOnlyList<string> AvailableMethods { get; } =
        ["NRV2E (padrão)", "NRV2B (mais rápido)", "NRV2D (equilibrado)",
         "LZMA (melhor compressão)", "Zstd (moderno)"];

    public IReadOnlyList<string> AvailableFilters { get; } = ["Auto", "Nenhum"];

    public IReadOnlyList<string> OutputModes { get; } =
        ["Substituir original", "Mesmo diretório (sufixo)", "Diretório personalizado"];

    [ObservableProperty] private string _selectedMethod = "LZMA (melhor compressão)";
    [ObservableProperty] private int _level = 8;
    [ObservableProperty] private string _selectedFilter = "Auto";
    [ObservableProperty] private bool _backup;
    [ObservableProperty] private bool _force;
    [ObservableProperty] private bool _isExplorerIntegrationInstalled;
    [ObservableProperty] private string _explorerIntegrationStatusText = "Status: integração não instalada";
    [ObservableProperty] private string _explorerIntegrationStatusDetailText = "Status: integração não instalada";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomOutputPath))]
    private string _selectedOutputMode = "Substituir original";

    [ObservableProperty] private string _customOutputPath = string.Empty;
    [ObservableProperty] private bool _closeAfterContextMenuExecution;

    public bool IsCustomOutputPath => SelectedOutputMode == "Diretório personalizado";

    public OptionsViewModel(
        IDialogService dialogService,
        IExplorerContextMenuService explorerContextMenuService,
        IAppSettingsService appSettingsService)
    {
        _dialogService = dialogService;
        _explorerContextMenuService = explorerContextMenuService;
        _appSettingsService = appSettingsService;

        CloseAfterContextMenuExecution = _appSettingsService
            .Load()
            .CloseAfterContextMenuExecution;
        RefreshExplorerIntegrationStatus();
    }

    [RelayCommand]
    private void BrowseOutput()
    {
        string? path = _dialogService.OpenFolder("Selecionar diretório de saída");
        if (!string.IsNullOrWhiteSpace(path))
            CustomOutputPath = path;
    }

    public Options ToOptions() => new()
    {
        Method = SelectedMethod switch
        {
            "LZMA (melhor compressão)" => CompressionMethod.Lzma,
            "Zstd (moderno)" => CompressionMethod.Zstd,
            "NRV2B (mais rápido)" => CompressionMethod.Nrv2bLE32,
            "NRV2D (equilibrado)" => CompressionMethod.Nrv2dLE32,
            _ => CompressionMethod.Nrv2eLE32,
        },
        Level = Level,
        Force = Force,
        KeepBackup = Backup,
        UseFilters = SelectedFilter == "Auto",
    };

    public FileProcessingRequest ToProcessingRequest(UpxCommand command)
    {
        var options = ToOptions();
        options.Command = command;

        FileOutputMode outputMode = SelectedOutputMode switch
        {
            "Mesmo diretório (sufixo)" => FileOutputMode.SameDirectoryWithSuffix,
            "Diretório personalizado" => FileOutputMode.CustomDirectory,
            _ => FileOutputMode.ReplaceOriginal,
        };

        return new FileProcessingRequest(
            options,
            outputMode,
            outputMode == FileOutputMode.CustomDirectory
                ? CustomOutputPath
                : null);
    }

    [RelayCommand]
    private void InstallExplorerIntegration()
    {
        _explorerContextMenuService.InstallForCurrentUser(autoPack: true);
        RefreshExplorerIntegrationStatus();
    }

    [RelayCommand]
    private void RemoveExplorerIntegration()
    {
        _explorerContextMenuService.RemoveForCurrentUser();
        RefreshExplorerIntegrationStatus();
    }

    public void RefreshExplorerIntegrationStatus()
    {
        ExplorerIntegrationStatus status = _explorerContextMenuService.GetStatus();
        IsExplorerIntegrationInstalled = status != ExplorerIntegrationStatus.NotInstalled;
        string statusText = status switch
        {
            ExplorerIntegrationStatus.ShellExtensionInstalled
                => "Status: integração do Windows Explorer instalada",
            ExplorerIntegrationStatus.ClassicInstalled
                => "Status: integração clássica instalada",
            _ => "Status: integração não instalada",
        };
        string detail = _explorerContextMenuService.LastInstallMessage;
        ExplorerIntegrationStatusDetailText = string.IsNullOrWhiteSpace(detail)
            ? statusText
            : $"{statusText} — {detail}";

        ExplorerIntegrationStatusText = string.IsNullOrWhiteSpace(detail)
            ? statusText
            : $"{statusText}{Environment.NewLine}{BuildShortExplorerIntegrationMessage(detail)}";
    }

    private static string BuildShortExplorerIntegrationMessage(string detail)
    {
        if (detail.Contains("Modo de Desenvolvedor", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("sideload", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("MSIX", StringComparison.OrdinalIgnoreCase))
        {
            return "Moderno bloqueado: MSIX ausente, não assinado ou certificado não confiável.";
        }

        if (detail.Contains("comhost", StringComparison.OrdinalIgnoreCase))
            return "Moderno indisponível: comhost não encontrado.";

        if (detail.Contains("sparse package", StringComparison.OrdinalIgnoreCase))
            return "Moderno indisponível: pacote sparse ausente.";

        return detail.Length <= 80
            ? detail
            : $"{detail[..77]}...";
    }

    partial void OnCloseAfterContextMenuExecutionChanged(bool value)
        => _appSettingsService.Save(new AppSettings(value));
}
