# UPX Sharp — Documentação da Camada Visual WPF (.NET 10)

> **Projeto:** UpxSharp — Interface Desktop WPF
> **Framework:** WPF / .NET 10 / C# 13
> **Padrão arquitetural:** MVVM (Model-View-ViewModel)
> **Finalidade:** Comprimir e descomprimir executáveis visualmente

---

## Sumário

1. [Visão Geral da UI](#1-visão-geral-da-ui)
2. [Estrutura do Projeto WPF](#2-estrutura-do-projeto-wpf)
3. [Padrão MVVM Adotado](#3-padrão-mvvm-adotado)
4. [Design System e Tema Visual](#4-design-system-e-tema-visual)
5. [MainWindow — Janela Principal](#5-mainwindow--janela-principal)
6. [FileDropPanel — Área de Drop de Arquivos](#6-filedroppanel--área-de-drop-de-arquivos)
7. [FileListView — Lista de Arquivos](#7-filelistview--lista-de-arquivos)
8. [OptionsPanel — Painel de Configurações](#8-optionspanel--painel-de-configurações)
9. [ProgressPanel — Progresso e Logs](#9-progresspanel--progresso-e-logs)
10. [ViewModels](#10-viewmodels)
11. [Commands e Behaviors](#11-commands-e-behaviors)
12. [Converters](#12-converters)
13. [Integração com UpxSharp Core](#13-integração-com-upxsharp-core)
14. [Checklist de Implementação Visual](#14-checklist-de-implementação-visual)

---

## 1. Visão Geral da UI

### Layout Geral da Janela

```
╔══════════════════════════════════════════════════════════════════╗
║  UpxSharp                                          [─] [□] [✕]  ║  ← TitleBar custom
╠══════════════════════════════════════════════════════════════════╣
║  [⬆ Comprimir]  [⬇ Descomprimir]  [🔍 Testar]   [🗑 Limpar]   ║  ← ToolBar
╠═══════════════════════════════════════╦══════════════════════════╣
║                                       ║                          ║
║   ┌───────────────────────────────┐   ║   COMPRESSÃO             ║
║   │  ⬇  Arraste arquivos aqui    │   ║  ─────────────────────   ║
║   │     ou clique para abrir      │   ║  Método:  [NRV2E   ▾]   ║
║   └───────────────────────────────┘   ║  Nível:   ●●●●●●●○○○    ║
║                                       ║  Filtro:  [Auto    ▾]   ║
║  ┌────┬──────────────┬──────┬─────┐   ║                          ║
║  │ St │ Arquivo      │ Tam. │ Red.│   ║  ☑ Backup original       ║
║  ├────┼──────────────┼──────┼─────┤   ║  ☐ Forçar               ║
║  │ ✔  │ app.exe      │ 1.2M │ 60% │   ║  ☐ Brute-force          ║
║  │ ⏳ │ game.exe     │ 8.4M │ ... │   ║                          ║
║  │ ✘  │ tool.dll     │ 560K │  —  │   ║  SAÍDA                   ║
║  └────┴──────────────┴──────┴─────┘   ║  [Substituir original ▾] ║
╠═══════════════════════════════════════╩══════════════════════════╣
║  game.exe ████████████████░░░░  64%                    3.2 MB/s  ║
║  ✔ app.exe: 1.2 MB → 480 KB (60% redução) em 0.3s              ║
║  ⏳ game.exe: comprimindo com NRV2E nível 8...                   ║
║  ✘ tool.dll: formato não reconhecido                             ║
╚══════════════════════════════════════════════════════════════════╝
```

### Estados da Aplicação

| Estado | Descrição | UI |
|---|---|---|
| `Idle` | Aguardando arquivos | Drop zone visível, botões desabilitados |
| `FilesLoaded` | Arquivos na fila | Botões habilitados, lista populada |
| `Processing` | Processando | Progresso ativo, botões bloqueados |
| `Done` | Concluído | Resultados exibidos na lista |
| `Error` | Erro parcial | Itens com erro marcados em vermelho |

---

## 2. Estrutura do Projeto WPF

```
UpxSharp.Wpf/
├── UpxSharp.Wpf.csproj
├── App.xaml                         ← ResourceDictionaries globais
├── App.xaml.cs                      ← Startup, DI container
│
├── Assets/
│   └── Icons/
│       ├── compress.png
│       ├── decompress.png
│       └── upxsharp-logo.ico
│
├── Themes/
│   ├── Colors.xaml                  ← Paleta de cores
│   ├── Typography.xaml              ← Estilos de texto
│   ├── Controls.xaml                ← Estilos de Button, ComboBox, etc.
│   ├── DataGrid.xaml                ← Estilo da lista de arquivos
│   └── Animations.xaml              ← Storyboards e transições
│
├── Views/
│   ├── MainWindow.xaml
│   ├── MainWindow.xaml.cs
│   └── Controls/
│       ├── FileDropPanel.xaml
│       ├── FileDropPanel.xaml.cs
│       ├── FileListView.xaml
│       ├── FileListView.xaml.cs
│       ├── OptionsPanel.xaml
│       ├── OptionsPanel.xaml.cs
│       ├── ProgressPanel.xaml
│       └── ProgressPanel.xaml.cs
│
├── ViewModels/
│   ├── MainViewModel.cs
│   ├── FileItemViewModel.cs
│   ├── OptionsViewModel.cs
│   └── ProgressViewModel.cs
│
├── Commands/
│   ├── RelayCommand.cs
│   └── AsyncRelayCommand.cs
│
├── Converters/
│   ├── FileSizeConverter.cs
│   ├── StatusToIconConverter.cs
│   ├── StatusToColorConverter.cs
│   └── BoolToVisibilityConverter.cs
│
├── Behaviors/
│   ├── FileDragDropBehavior.cs
│   └── AutoScrollBehavior.cs
│
└── Services/
    ├── IFileProcessingService.cs
    ├── FileProcessingService.cs
    └── IDialogService.cs
```

### UpxSharp.Wpf.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <LangVersion>13</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <ApplicationIcon>Assets\Icons\upxsharp-logo.ico</ApplicationIcon>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\UpxSharp\UpxSharp.csproj" />
  </ItemGroup>

  <ItemGroup>
    <!-- MVVM Source Generators: ObservableProperty, RelayCommand -->
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.*" />
    <!-- DI Container leve -->
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.*" />
    <!-- Behaviors via Microsoft.Xaml.Behaviors.Wpf -->
    <PackageReference Include="Microsoft.Xaml.Behaviors.Wpf" Version="1.*" />
  </ItemGroup>
</Project>
```

---

## 3. Padrão MVVM Adotado

Usa **CommunityToolkit.Mvvm** para eliminar boilerplate via source generators:

```csharp
// ViewModels/FileItemViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;

namespace UpxSharp.Wpf.ViewModels;

public partial class FileItemViewModel : ObservableObject
{
    [ObservableProperty] private string _filePath    = string.Empty;
    [ObservableProperty] private string _fileName    = string.Empty;
    [ObservableProperty] private long   _originalSize;
    [ObservableProperty] private long   _compressedSize;
    [ObservableProperty] private double _ratio;
    [ObservableProperty] private FileProcessingStatus _status = FileProcessingStatus.Pending;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private double _progress;

    public string OriginalSizeFormatted   => FormatSize(OriginalSize);
    public string CompressedSizeFormatted => FormatSize(CompressedSize);
    public string RatioFormatted          => Ratio > 0 ? $"{Ratio * 100:F1}% menor" : "—";

    partial void OnOriginalSizeChanged(long value)
        => OnPropertyChanged(nameof(OriginalSizeFormatted));

    partial void OnCompressedSizeChanged(long value)
    {
        OnPropertyChanged(nameof(CompressedSizeFormatted));
        if (OriginalSize > 0)
            Ratio = 1.0 - (double)CompressedSize / OriginalSize;
        OnPropertyChanged(nameof(RatioFormatted));
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        <= 0                  => "—",
        < 1024                => $"{bytes} B",
        < 1024 * 1024         => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _                     => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
    };
}

public enum FileProcessingStatus { Pending, Processing, Done, Error, Skipped }
```

### App.xaml.cs — Injeção de Dependências

```csharp
public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var svc = new ServiceCollection();

        svc.AddSingleton<IFileProcessingService, FileProcessingService>();
        svc.AddSingleton<IDialogService, DialogService>();
        svc.AddSingleton<OptionsViewModel>();
        svc.AddSingleton<ProgressViewModel>();
        svc.AddSingleton<MainViewModel>();
        svc.AddSingleton<MainWindow>();

        Services = svc.BuildServiceProvider();

        var win = Services.GetRequiredService<MainWindow>();
        win.DataContext = Services.GetRequiredService<MainViewModel>();
        win.Show();
    }
}
```

---

## 4. Design System e Tema Visual

### Colors.xaml — Paleta escura profissional

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- Fundos -->
    <Color x:Key="Background.Deep">#FF0E1117</Color>
    <Color x:Key="Background.Surface">#FF161B22</Color>
    <Color x:Key="Background.Elevated">#FF1C2128</Color>
    <Color x:Key="Background.Hover">#FF22272E</Color>
    <Color x:Key="Background.Active">#FF2D333B</Color>

    <!-- Texto -->
    <Color x:Key="Text.Primary">#FFE6EDF3</Color>
    <Color x:Key="Text.Secondary">#FF7D8590</Color>
    <Color x:Key="Text.Muted">#FF484F58</Color>

    <!-- Acento: azul ciano (ação primária) -->
    <Color x:Key="Accent.Primary">#FF1F6FEB</Color>
    <Color x:Key="Accent.Hover">#FF388BFD</Color>
    <Color x:Key="Accent.Pressed">#FF1158C7</Color>

    <!-- Status -->
    <Color x:Key="Status.Success">#FF3FB950</Color>
    <Color x:Key="Status.Error">#FFF85149</Color>
    <Color x:Key="Status.Warning">#FFD29922</Color>
    <Color x:Key="Status.Processing">#FF58A6FF</Color>

    <!-- Bordas -->
    <Color x:Key="Border.Default">#FF30363D</Color>
    <Color x:Key="Border.Subtle">#FF21262D</Color>

    <!-- Brushes derivados -->
    <SolidColorBrush x:Key="Brush.Background.Deep"     Color="{StaticResource Background.Deep}" />
    <SolidColorBrush x:Key="Brush.Background.Surface"  Color="{StaticResource Background.Surface}" />
    <SolidColorBrush x:Key="Brush.Background.Elevated" Color="{StaticResource Background.Elevated}" />
    <SolidColorBrush x:Key="Brush.Background.Hover"    Color="{StaticResource Background.Hover}" />
    <SolidColorBrush x:Key="Brush.Background.Active"   Color="{StaticResource Background.Active}" />
    <SolidColorBrush x:Key="Brush.Text.Primary"        Color="{StaticResource Text.Primary}" />
    <SolidColorBrush x:Key="Brush.Text.Secondary"      Color="{StaticResource Text.Secondary}" />
    <SolidColorBrush x:Key="Brush.Text.Muted"          Color="{StaticResource Text.Muted}" />
    <SolidColorBrush x:Key="Brush.Accent.Primary"      Color="{StaticResource Accent.Primary}" />
    <SolidColorBrush x:Key="Brush.Accent.Hover"        Color="{StaticResource Accent.Hover}" />
    <SolidColorBrush x:Key="Brush.Accent.Pressed"      Color="{StaticResource Accent.Pressed}" />
    <SolidColorBrush x:Key="Brush.Status.Success"      Color="{StaticResource Status.Success}" />
    <SolidColorBrush x:Key="Brush.Status.Error"        Color="{StaticResource Status.Error}" />
    <SolidColorBrush x:Key="Brush.Status.Warning"      Color="{StaticResource Status.Warning}" />
    <SolidColorBrush x:Key="Brush.Status.Processing"   Color="{StaticResource Status.Processing}" />
    <SolidColorBrush x:Key="Brush.Border.Default"      Color="{StaticResource Border.Default}" />
    <SolidColorBrush x:Key="Brush.Border.Subtle"       Color="{StaticResource Border.Subtle}" />
</ResourceDictionary>
```

### Typography.xaml

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:sys="clr-namespace:System;assembly=mscorlib">

    <FontFamily x:Key="Font.Display">Segoe UI Variable Display, Segoe UI, Sans-Serif</FontFamily>
    <FontFamily x:Key="Font.Body">Segoe UI Variable Text, Segoe UI, Sans-Serif</FontFamily>
    <FontFamily x:Key="Font.Mono">Cascadia Code, Consolas, Monospace</FontFamily>

    <sys:Double x:Key="FontSize.XS">10</sys:Double>
    <sys:Double x:Key="FontSize.SM">12</sys:Double>
    <sys:Double x:Key="FontSize.MD">13</sys:Double>
    <sys:Double x:Key="FontSize.LG">15</sys:Double>
    <sys:Double x:Key="FontSize.XL">20</sys:Double>

    <Style x:Key="Text.Body" TargetType="TextBlock">
        <Setter Property="FontFamily" Value="{StaticResource Font.Body}" />
        <Setter Property="FontSize"   Value="{StaticResource FontSize.MD}" />
        <Setter Property="Foreground" Value="{StaticResource Brush.Text.Primary}" />
    </Style>

    <Style x:Key="Text.Label" TargetType="TextBlock">
        <Setter Property="FontFamily" Value="{StaticResource Font.Body}" />
        <Setter Property="FontSize"   Value="{StaticResource FontSize.SM}" />
        <Setter Property="FontWeight" Value="Medium" />
        <Setter Property="Foreground" Value="{StaticResource Brush.Text.Secondary}" />
    </Style>

    <Style x:Key="Text.Mono" TargetType="TextBlock">
        <Setter Property="FontFamily" Value="{StaticResource Font.Mono}" />
        <Setter Property="FontSize"   Value="{StaticResource FontSize.SM}" />
        <Setter Property="Foreground" Value="{StaticResource Brush.Text.Secondary}" />
    </Style>

    <Style x:Key="Text.Success" TargetType="TextBlock" BasedOn="{StaticResource Text.Mono}">
        <Setter Property="Foreground" Value="{StaticResource Brush.Status.Success}" />
    </Style>

    <Style x:Key="Text.Error" TargetType="TextBlock" BasedOn="{StaticResource Text.Mono}">
        <Setter Property="Foreground" Value="{StaticResource Brush.Status.Error}" />
    </Style>
</ResourceDictionary>
```

### Controls.xaml — Estilos de controles

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- Button primário -->
    <Style x:Key="Button.Primary" TargetType="Button">
        <Setter Property="Background"      Value="{StaticResource Brush.Accent.Primary}" />
        <Setter Property="Foreground"      Value="{StaticResource Brush.Text.Primary}" />
        <Setter Property="BorderThickness" Value="0" />
        <Setter Property="Padding"         Value="16,8" />
        <Setter Property="FontSize"        Value="{StaticResource FontSize.MD}" />
        <Setter Property="FontWeight"      Value="Medium" />
        <Setter Property="Cursor"          Value="Hand" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Border Background="{TemplateBinding Background}"
                            CornerRadius="6"
                            Padding="{TemplateBinding Padding}">
                        <ContentPresenter HorizontalAlignment="Center"
                                          VerticalAlignment="Center" />
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter Property="Background" Value="{StaticResource Brush.Accent.Hover}" />
                        </Trigger>
                        <Trigger Property="IsPressed" Value="True">
                            <Setter Property="Background" Value="{StaticResource Brush.Accent.Pressed}" />
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Background" Value="{StaticResource Brush.Background.Active}" />
                            <Setter Property="Foreground" Value="{StaticResource Brush.Text.Muted}" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Button secundário (ghost) -->
    <Style x:Key="Button.Ghost" TargetType="Button" BasedOn="{StaticResource Button.Primary}">
        <Setter Property="Background"      Value="Transparent" />
        <Setter Property="BorderBrush"     Value="{StaticResource Brush.Border.Default}" />
        <Setter Property="BorderThickness" Value="1" />
        <Style.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
                <Setter Property="Background" Value="{StaticResource Brush.Background.Hover}" />
            </Trigger>
        </Style.Triggers>
    </Style>

    <!-- ComboBox dark -->
    <Style x:Key="ComboBox.Dark" TargetType="ComboBox">
        <Setter Property="Background"      Value="{StaticResource Brush.Background.Surface}" />
        <Setter Property="Foreground"      Value="{StaticResource Brush.Text.Primary}" />
        <Setter Property="BorderBrush"     Value="{StaticResource Brush.Border.Default}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="Padding"         Value="8,4" />
        <Setter Property="FontSize"        Value="{StaticResource FontSize.MD}" />
    </Style>

    <!-- CheckBox dark -->
    <Style x:Key="CheckBox.Dark" TargetType="CheckBox">
        <Setter Property="Foreground" Value="{StaticResource Brush.Text.Primary}" />
        <Setter Property="FontSize"   Value="{StaticResource FontSize.MD}" />
        <Setter Property="Margin"     Value="0,4" />
    </Style>

    <!-- ProgressBar slim -->
    <Style x:Key="ProgressBar.Accent" TargetType="ProgressBar">
        <Setter Property="Height"          Value="4" />
        <Setter Property="Background"      Value="{StaticResource Brush.Background.Active}" />
        <Setter Property="Foreground"      Value="{StaticResource Brush.Accent.Primary}" />
        <Setter Property="BorderThickness" Value="0" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ProgressBar">
                    <Grid>
                        <Border Background="{TemplateBinding Background}" CornerRadius="2" />
                        <Border x:Name="PART_Track" />
                        <Border x:Name="PART_Indicator"
                                Background="{TemplateBinding Foreground}"
                                HorizontalAlignment="Left"
                                CornerRadius="2" />
                    </Grid>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Slider nível de compressão -->
    <Style x:Key="Slider.Level" TargetType="Slider">
        <Setter Property="Minimum"             Value="1" />
        <Setter Property="Maximum"             Value="9" />
        <Setter Property="TickFrequency"       Value="1" />
        <Setter Property="IsSnapToTickEnabled" Value="True" />
        <Setter Property="TickPlacement"       Value="BottomRight" />
    </Style>
</ResourceDictionary>
```

### App.xaml — merge de todos os dicionários

```xml
<Application xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:conv="clr-namespace:UpxSharp.Wpf.Converters"
             x:Class="UpxSharp.Wpf.App">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="Themes/Colors.xaml" />
                <ResourceDictionary Source="Themes/Typography.xaml" />
                <ResourceDictionary Source="Themes/Controls.xaml" />
                <ResourceDictionary Source="Themes/DataGrid.xaml" />
                <ResourceDictionary Source="Themes/Animations.xaml" />
            </ResourceDictionary.MergedDictionaries>

            <!-- Converters globais -->
            <conv:BoolToVisibilityConverter x:Key="BoolToVisibilityConverter" />
            <conv:BoolToVisibilityConverter x:Key="InvertedBoolToVisibilityConverter" Invert="True" />
            <conv:FileSizeConverter         x:Key="FileSizeConverter" />
            <conv:StatusToIconConverter     x:Key="StatusToIconConverter" />
            <conv:StatusToColorConverter    x:Key="StatusToColorConverter" />
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

---

## 5. MainWindow — Janela Principal

### MainWindow.xaml

```xml
<Window x:Class="UpxSharp.Wpf.Views.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:controls="clr-namespace:UpxSharp.Wpf.Views.Controls"
        Title="UpxSharp" Width="1000" Height="680"
        MinWidth="800" MinHeight="500"
        WindowStyle="None" AllowsTransparency="True"
        Background="{StaticResource Brush.Background.Deep}"
        ResizeMode="CanResizeWithGrip">

    <WindowChrome.WindowChrome>
        <WindowChrome CaptionHeight="40" ResizeBorderThickness="6"
                      GlassFrameThickness="0" CornerRadius="8" />
    </WindowChrome.WindowChrome>

    <Border CornerRadius="8"
            BorderBrush="{StaticResource Brush.Border.Default}"
            BorderThickness="1">
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="40" />     <!-- TitleBar -->
                <RowDefinition Height="52" />     <!-- ToolBar -->
                <RowDefinition Height="*" />      <!-- Conteúdo -->
                <RowDefinition Height="Auto" />   <!-- ProgressPanel -->
            </Grid.RowDefinitions>

            <!-- TitleBar -->
            <Grid Grid.Row="0" Background="{StaticResource Brush.Background.Elevated}">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>

                <StackPanel Grid.Column="0" Orientation="Horizontal"
                            Margin="16,0" VerticalAlignment="Center">
                    <Image Source="/Assets/Icons/upxsharp-logo.ico"
                           Width="18" Height="18" Margin="0,0,8,0" />
                    <TextBlock Text="UpxSharp" Style="{StaticResource Text.Body}"
                               FontWeight="SemiBold" VerticalAlignment="Center" />
                </StackPanel>

                <Rectangle Grid.Column="1" Fill="Transparent"
                           MouseLeftButtonDown="TitleBar_MouseDown" />

                <StackPanel Grid.Column="2" Orientation="Horizontal"
                            WindowChrome.IsHitTestVisibleInChrome="True">
                    <Button Content="─" Width="46" Height="40"
                            Style="{StaticResource Button.Ghost}"
                            BorderThickness="0"
                            Click="MinimizeButton_Click" />
                    <Button Content="□" Width="46" Height="40"
                            Style="{StaticResource Button.Ghost}"
                            BorderThickness="0"
                            Click="MaximizeButton_Click" />
                    <Button Content="✕" Width="46" Height="40"
                            Style="{StaticResource Button.Ghost}"
                            BorderThickness="0"
                            Click="CloseButton_Click" />
                </StackPanel>
            </Grid>

            <!-- ToolBar -->
            <Border Grid.Row="1"
                    Background="{StaticResource Brush.Background.Elevated}"
                    BorderBrush="{StaticResource Brush.Border.Subtle}"
                    BorderThickness="0,0,0,1" Padding="12,0">
                <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                    <Button Content="⬆  Comprimir"
                            Style="{StaticResource Button.Primary}"
                            Command="{Binding CompressCommand}"
                            Margin="0,0,8,0" />
                    <Button Content="⬇  Descomprimir"
                            Style="{StaticResource Button.Ghost}"
                            Command="{Binding DecompressCommand}"
                            Margin="0,0,8,0" />
                    <Button Content="🔍  Testar"
                            Style="{StaticResource Button.Ghost}"
                            Command="{Binding TestCommand}"
                            Margin="0,0,8,0" />
                    <Separator Width="1" Background="{StaticResource Brush.Border.Default}"
                               Margin="8,8" />
                    <Button Content="🗑  Limpar"
                            Style="{StaticResource Button.Ghost}"
                            Command="{Binding ClearFilesCommand}"
                            IsEnabled="{Binding HasFiles}" />
                </StackPanel>
            </Border>

            <!-- Conteúdo principal -->
            <Grid Grid.Row="2">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="1" />
                    <ColumnDefinition Width="260" />
                </Grid.ColumnDefinitions>

                <!-- Esquerda: drop zone OU lista -->
                <Grid Grid.Column="0">
                    <controls:FileDropPanel
                        Visibility="{Binding HasFiles,
                            Converter={StaticResource InvertedBoolToVisibilityConverter}}"
                        DataContext="{Binding}" />

                    <controls:FileListView
                        ItemsSource="{Binding Files}"
                        Visibility="{Binding HasFiles,
                            Converter={StaticResource BoolToVisibilityConverter}}" />
                </Grid>

                <!-- Divisor -->
                <Rectangle Grid.Column="1" Fill="{StaticResource Brush.Border.Subtle}" />

                <!-- Direita: opções -->
                <controls:OptionsPanel Grid.Column="2"
                    DataContext="{Binding OptionsViewModel}" />
            </Grid>

            <!-- Progress + Log -->
            <controls:ProgressPanel Grid.Row="3"
                DataContext="{Binding ProgressViewModel}" />
        </Grid>
    </Border>
</Window>
```

### MainWindow.xaml.cs

```csharp
public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal : WindowState.Maximized;
        else
            DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => Close();
}
```

---

## 6. FileDropPanel — Área de Drop de Arquivos

### FileDropPanel.xaml

```xml
<UserControl x:Class="UpxSharp.Wpf.Views.Controls.FileDropPanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             AllowDrop="True"
             Drop="OnDrop" DragOver="OnDragOver"
             DragEnter="OnDragEnter" DragLeave="OnDragLeave">

    <Border Margin="16" MinHeight="160" CornerRadius="8"
            BorderThickness="2" BorderBrush="{StaticResource Brush.Border.Default}"
            Background="{StaticResource Brush.Background.Surface}">
        <Border.Style>
            <Style TargetType="Border">
                <Style.Triggers>
                    <DataTrigger Binding="{Binding IsDragOver}" Value="True">
                        <Setter Property="BorderBrush"
                                Value="{StaticResource Brush.Accent.Primary}" />
                        <Setter Property="Background"
                                Value="{StaticResource Brush.Background.Hover}" />
                    </DataTrigger>
                </Style.Triggers>
            </Style>
        </Border.Style>

        <StackPanel VerticalAlignment="Center" HorizontalAlignment="Center" Margin="24">
            <TextBlock Text="⬇" FontSize="36" HorizontalAlignment="Center"
                       Foreground="{StaticResource Brush.Text.Muted}" Margin="0,0,0,12">
                <TextBlock.Style>
                    <Style TargetType="TextBlock">
                        <Style.Triggers>
                            <DataTrigger Binding="{Binding IsDragOver}" Value="True">
                                <Setter Property="Foreground"
                                        Value="{StaticResource Brush.Accent.Primary}" />
                            </DataTrigger>
                        </Style.Triggers>
                    </Style>
                </TextBlock.Style>
            </TextBlock>

            <TextBlock Text="Arraste executáveis aqui"
                       Style="{StaticResource Text.Body}"
                       FontWeight="SemiBold"
                       HorizontalAlignment="Center" />

            <TextBlock Text=".exe  .dll  .so  .elf  .bin"
                       Style="{StaticResource Text.Label}"
                       HorizontalAlignment="Center"
                       Margin="0,4,0,16" />

            <Button Content="Ou clique para selecionar arquivos"
                    Style="{StaticResource Button.Ghost}"
                    Command="{Binding BrowseFilesCommand}"
                    HorizontalAlignment="Center" />
        </StackPanel>
    </Border>
</UserControl>
```

### FileDropPanel.xaml.cs

```csharp
public partial class FileDropPanel : UserControl
{
    public FileDropPanel() => InitializeComponent();

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
            DataContext is MainViewModel vm)
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            vm.FilesDroppedCommand.Execute(files);
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
        => e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy : DragDropEffects.None;

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.IsDragOver = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.IsDragOver = false;
    }
}
```

---

## 7. FileListView — Lista de Arquivos

### FileListView.xaml

```xml
<UserControl x:Class="UpxSharp.Wpf.Views.Controls.FileListView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <DataGrid x:Name="FileGrid"
              AutoGenerateColumns="False"
              SelectionMode="Extended"
              CanUserAddRows="False"
              CanUserDeleteRows="False"
              GridLinesVisibility="Horizontal"
              HeadersVisibility="Column"
              Background="{StaticResource Brush.Background.Deep}"
              RowBackground="{StaticResource Brush.Background.Deep}"
              AlternatingRowBackground="{StaticResource Brush.Background.Surface}"
              BorderThickness="0"
              Foreground="{StaticResource Brush.Text.Primary}"
              FontSize="{StaticResource FontSize.MD}"
              RowHeight="36">

        <DataGrid.Columns>

            <!-- Ícone de status -->
            <DataGridTemplateColumn Width="40" CanUserSort="False">
                <DataGridTemplateColumn.CellTemplate>
                    <DataTemplate>
                        <TextBlock
                            Text="{Binding Status, Converter={StaticResource StatusToIconConverter}}"
                            Foreground="{Binding Status, Converter={StaticResource StatusToColorConverter}}"
                            HorizontalAlignment="Center" VerticalAlignment="Center"
                            FontSize="14" />
                    </DataTemplate>
                </DataGridTemplateColumn.CellTemplate>
            </DataGridTemplateColumn>

            <!-- Nome do arquivo -->
            <DataGridTextColumn Header="Arquivo"
                                Binding="{Binding FileName}"
                                Width="*" IsReadOnly="True" />

            <!-- Tamanho original -->
            <DataGridTextColumn Header="Tamanho"
                                Binding="{Binding OriginalSizeFormatted}"
                                Width="90" IsReadOnly="True">
                <DataGridTextColumn.ElementStyle>
                    <Style TargetType="TextBlock">
                        <Setter Property="Foreground" Value="{StaticResource Brush.Text.Secondary}" />
                        <Setter Property="HorizontalAlignment" Value="Right" />
                        <Setter Property="Margin" Value="0,0,8,0" />
                    </Style>
                </DataGridTextColumn.ElementStyle>
            </DataGridTextColumn>

            <!-- Tamanho comprimido -->
            <DataGridTextColumn Header="Comprimido"
                                Binding="{Binding CompressedSizeFormatted}"
                                Width="100" IsReadOnly="True">
                <DataGridTextColumn.ElementStyle>
                    <Style TargetType="TextBlock">
                        <Setter Property="HorizontalAlignment" Value="Right" />
                        <Setter Property="Margin" Value="0,0,8,0" />
                    </Style>
                </DataGridTextColumn.ElementStyle>
            </DataGridTextColumn>

            <!-- Redução percentual -->
            <DataGridTemplateColumn Header="Redução" Width="90">
                <DataGridTemplateColumn.CellTemplate>
                    <DataTemplate>
                        <TextBlock Text="{Binding RatioFormatted}"
                                   Foreground="{StaticResource Brush.Status.Success}"
                                   HorizontalAlignment="Right"
                                   VerticalAlignment="Center"
                                   Margin="0,0,8,0">
                            <TextBlock.Style>
                                <Style TargetType="TextBlock">
                                    <Style.Triggers>
                                        <DataTrigger Binding="{Binding Status}" Value="Error">
                                            <Setter Property="Foreground"
                                                    Value="{StaticResource Brush.Status.Error}" />
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </TextBlock.Style>
                        </TextBlock>
                    </DataTemplate>
                </DataGridTemplateColumn.CellTemplate>
            </DataGridTemplateColumn>

            <!-- Progresso inline (visível só durante processamento) -->
            <DataGridTemplateColumn Header="" Width="120">
                <DataGridTemplateColumn.CellTemplate>
                    <DataTemplate>
                        <ProgressBar Style="{StaticResource ProgressBar.Accent}"
                                     Value="{Binding Progress}" Maximum="100"
                                     VerticalAlignment="Center">
                            <ProgressBar.Style>
                                <Style TargetType="ProgressBar"
                                       BasedOn="{StaticResource ProgressBar.Accent}">
                                    <Setter Property="Visibility" Value="Collapsed" />
                                    <Style.Triggers>
                                        <DataTrigger Binding="{Binding Status}" Value="Processing">
                                            <Setter Property="Visibility" Value="Visible" />
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </ProgressBar.Style>
                        </ProgressBar>
                    </DataTemplate>
                </DataGridTemplateColumn.CellTemplate>
            </DataGridTemplateColumn>

            <!-- Botão remover item -->
            <DataGridTemplateColumn Width="36" CanUserSort="False">
                <DataGridTemplateColumn.CellTemplate>
                    <DataTemplate>
                        <Button Content="✕" Width="28" Height="24"
                                Style="{StaticResource Button.Ghost}"
                                Command="{Binding DataContext.RemoveFileCommand,
                                    RelativeSource={RelativeSource AncestorType=DataGrid}}"
                                CommandParameter="{Binding}"
                                Padding="0" FontSize="10" Opacity="0.4">
                            <Button.Style>
                                <Style TargetType="Button" BasedOn="{StaticResource Button.Ghost}">
                                    <Style.Triggers>
                                        <Trigger Property="IsMouseOver" Value="True">
                                            <Setter Property="Opacity" Value="1" />
                                        </Trigger>
                                    </Style.Triggers>
                                </Style>
                            </Button.Style>
                        </Button>
                    </DataTemplate>
                </DataGridTemplateColumn.CellTemplate>
            </DataGridTemplateColumn>

        </DataGrid.Columns>
    </DataGrid>
</UserControl>
```

---

## 8. OptionsPanel — Painel de Configurações

### OptionsPanel.xaml

```xml
<UserControl x:Class="UpxSharp.Wpf.Views.Controls.OptionsPanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Background="{StaticResource Brush.Background.Surface}">

    <ScrollViewer VerticalScrollBarVisibility="Auto">
        <StackPanel Margin="16">

            <!-- Seção: Compressão -->
            <TextBlock Text="COMPRESSÃO" Style="{StaticResource Text.Label}"
                       FontWeight="SemiBold" Margin="0,0,0,12" />

            <TextBlock Text="Método" Style="{StaticResource Text.Label}" Margin="0,0,0,4" />
            <ComboBox Style="{StaticResource ComboBox.Dark}"
                      ItemsSource="{Binding AvailableMethods}"
                      SelectedItem="{Binding SelectedMethod}"
                      Margin="0,0,0,12" />

            <Grid Margin="0,0,0,4">
                <TextBlock Text="Nível" Style="{StaticResource Text.Label}" />
                <TextBlock Text="{Binding Level}" Style="{StaticResource Text.Label}"
                           HorizontalAlignment="Right" />
            </Grid>
            <Slider Style="{StaticResource Slider.Level}"
                    Value="{Binding Level}" Margin="0,0,0,12" />

            <TextBlock Text="Filtro de código" Style="{StaticResource Text.Label}" Margin="0,0,0,4" />
            <ComboBox Style="{StaticResource ComboBox.Dark}"
                      ItemsSource="{Binding AvailableFilters}"
                      SelectedItem="{Binding SelectedFilter}"
                      Margin="0,0,0,20" />

            <!-- Divisor -->
            <Rectangle Height="1" Fill="{StaticResource Brush.Border.Subtle}" Margin="0,0,0,16" />

            <!-- Seção: Opções -->
            <TextBlock Text="OPÇÕES" Style="{StaticResource Text.Label}"
                       FontWeight="SemiBold" Margin="0,0,0,12" />

            <CheckBox Content="Criar backup do original (.BAK)"
                      Style="{StaticResource CheckBox.Dark}"
                      IsChecked="{Binding Backup}" />
            <CheckBox Content="Forçar compressão"
                      Style="{StaticResource CheckBox.Dark}"
                      IsChecked="{Binding Force}" />
            <CheckBox Content="Modo brute-force (mais lento)"
                      Style="{StaticResource CheckBox.Dark}"
                      IsChecked="{Binding Brute}"
                      Margin="0,0,0,20" />

            <!-- Divisor -->
            <Rectangle Height="1" Fill="{StaticResource Brush.Border.Subtle}" Margin="0,0,0,16" />

            <!-- Seção: Saída -->
            <TextBlock Text="ARQUIVO DE SAÍDA" Style="{StaticResource Text.Label}"
                       FontWeight="SemiBold" Margin="0,0,0,12" />

            <ComboBox Style="{StaticResource ComboBox.Dark}"
                      ItemsSource="{Binding OutputModes}"
                      SelectedItem="{Binding SelectedOutputMode}"
                      Margin="0,0,0,8" />

            <!-- Caminho personalizado (visível apenas no modo customizado) -->
            <Grid Visibility="{Binding IsCustomOutputPath,
                      Converter={StaticResource BoolToVisibilityConverter}}">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <TextBox Grid.Column="0"
                         Text="{Binding CustomOutputPath}"
                         Background="{StaticResource Brush.Background.Surface}"
                         Foreground="{StaticResource Brush.Text.Primary}"
                         BorderBrush="{StaticResource Brush.Border.Default}"
                         BorderThickness="1" Padding="8,4"
                         FontSize="{StaticResource FontSize.SM}" />
                <Button Grid.Column="1" Content="…"
                        Style="{StaticResource Button.Ghost}"
                        Command="{Binding BrowseOutputCommand}"
                        Margin="4,0,0,0" Width="32" />
            </Grid>

        </StackPanel>
    </ScrollViewer>
</UserControl>
```

---

## 9. ProgressPanel — Progresso e Logs

### ProgressPanel.xaml

```xml
<UserControl x:Class="UpxSharp.Wpf.Views.Controls.ProgressPanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:b="clr-namespace:UpxSharp.Wpf.Behaviors">

    <Border Background="{StaticResource Brush.Background.Elevated}"
            BorderBrush="{StaticResource Brush.Border.Subtle}"
            BorderThickness="0,1,0,0" MaxHeight="200">
        <Grid Margin="12,8">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="*" MinHeight="60" />
            </Grid.RowDefinitions>

            <!-- Barra de progresso do arquivo atual -->
            <Grid Grid.Row="0" Margin="0,0,0,8"
                  Visibility="{Binding IsProcessing,
                      Converter={StaticResource BoolToVisibilityConverter}}">
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="Auto" />
                </Grid.RowDefinitions>

                <Grid Grid.Row="0" Margin="0,0,0,4">
                    <TextBlock Text="{Binding CurrentFileName}"
                               Style="{StaticResource Text.Body}" FontWeight="Medium" />
                    <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                        <TextBlock Text="{Binding ProgressPercent, StringFormat={}{0:F0}%}"
                                   Style="{StaticResource Text.Label}" Margin="0,0,12,0" />
                        <TextBlock Text="{Binding Speed}" Style="{StaticResource Text.Label}" />
                    </StackPanel>
                </Grid>

                <ProgressBar Grid.Row="1"
                             Style="{StaticResource ProgressBar.Accent}"
                             Value="{Binding ProgressPercent}" Maximum="100" />
            </Grid>

            <!-- Log -->
            <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto"
                          b:AutoScrollBehavior.IsEnabled="True">
                <ItemsControl ItemsSource="{Binding LogEntries}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <TextBlock Text="{Binding Message}"
                                       Style="{StaticResource Text.Mono}"
                                       Margin="0,1" />
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </ScrollViewer>

        </Grid>
    </Border>
</UserControl>
```

---

## 10. ViewModels

### MainViewModel.cs

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace UpxSharp.Wpf.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IFileProcessingService _processingService;
    private readonly IDialogService         _dialogService;

    public OptionsViewModel  OptionsViewModel  { get; }
    public ProgressViewModel ProgressViewModel { get; }

    public ObservableCollection<FileItemViewModel> Files { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFiles))]
    private AppState _state = AppState.Idle;

    [ObservableProperty] private bool _isDragOver;

    public bool HasFiles     => Files.Count > 0;
    public bool IsProcessing => State == AppState.Processing;

    public MainViewModel(
        IFileProcessingService svc,
        IDialogService dlg,
        OptionsViewModel optVm,
        ProgressViewModel progVm)
    {
        _processingService = svc;
        _dialogService     = dlg;
        OptionsViewModel   = optVm;
        ProgressViewModel  = progVm;
        Files.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasFiles));
    }

    [RelayCommand]
    private void FilesDropped(string[] paths)
    {
        foreach (var p in paths)
        {
            if (Files.Any(f => f.FilePath == p)) continue;
            Files.Add(new FileItemViewModel
            {
                FilePath     = p,
                FileName     = Path.GetFileName(p),
                OriginalSize = new FileInfo(p).Length,
                Status       = FileProcessingStatus.Pending,
            });
        }
        State = AppState.FilesLoaded;
    }

    [RelayCommand]
    private void BrowseFiles()
    {
        var paths = _dialogService.OpenFiles(
            "Selecionar executáveis",
            "Executáveis|*.exe;*.dll;*.so;*.elf;*.bin|Todos|*.*");
        if (paths?.Length > 0) FilesDropped(paths);
    }

    [RelayCommand(CanExecute = nameof(CanProcess))]
    private async Task CompressAsync(CancellationToken ct)
        => await ProcessAsync(UpxCommand.Compress, ct);

    [RelayCommand(CanExecute = nameof(CanProcess))]
    private async Task DecompressAsync(CancellationToken ct)
        => await ProcessAsync(UpxCommand.Decompress, ct);

    [RelayCommand(CanExecute = nameof(CanProcess))]
    private async Task TestAsync(CancellationToken ct)
        => await ProcessAsync(UpxCommand.Test, ct);

    private bool CanProcess() => HasFiles && State != AppState.Processing;

    private async Task ProcessAsync(UpxCommand cmd, CancellationToken ct)
    {
        State = AppState.Processing;
        ProgressViewModel.IsProcessing = true;
        ProgressViewModel.Clear();

        var options = OptionsViewModel.ToOptions();
        options.Command = cmd;

        try
        {
            await _processingService.ProcessAsync(
                Files, options,
                onProgress: (file, pct) =>
                {
                    file.Progress = pct;
                    ProgressViewModel.UpdateCurrentFile(file.FileName, pct);
                },
                onLog: (msg, lvl) => ProgressViewModel.AddLog(msg, lvl),
                ct);
        }
        finally
        {
            ProgressViewModel.IsProcessing = false;
            State = HasFiles ? AppState.Done : AppState.Idle;
        }
    }

    [RelayCommand]
    private void RemoveFile(FileItemViewModel file) => Files.Remove(file);

    [RelayCommand]
    private void ClearFiles()
    {
        Files.Clear();
        ProgressViewModel.Clear();
        State = AppState.Idle;
    }
}

public enum AppState { Idle, FilesLoaded, Processing, Done, Error }
```

### OptionsViewModel.cs

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

public partial class OptionsViewModel : ObservableObject
{
    public IReadOnlyList<string> AvailableMethods { get; } =
        ["NRV2E (padrão)", "NRV2B (mais rápido)", "NRV2D (equilibrado)",
         "LZMA (melhor compressão)", "Zstd (moderno)"];

    public IReadOnlyList<string> AvailableFilters { get; } =
        ["Auto", "Nenhum", "x86", "ARM", "ARM Thumb", "MIPS", "PowerPC"];

    public IReadOnlyList<string> OutputModes { get; } =
        ["Substituir original", "Mesmo diretório (sufixo)", "Diretório personalizado"];

    [ObservableProperty] private string _selectedMethod    = "NRV2E (padrão)";
    [ObservableProperty] private int    _level             = 8;
    [ObservableProperty] private string _selectedFilter    = "Auto";
    [ObservableProperty] private bool   _backup;
    [ObservableProperty] private bool   _force;
    [ObservableProperty] private bool   _brute;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomOutputPath))]
    private string _selectedOutputMode = "Substituir original";

    [ObservableProperty] private string _customOutputPath = string.Empty;

    public bool IsCustomOutputPath => SelectedOutputMode == "Diretório personalizado";

    [RelayCommand]
    private void BrowseOutput() { /* abrir FolderBrowserDialog */ }

    public Options ToOptions() => new()
    {
        Method = SelectedMethod switch
        {
            "LZMA (melhor compressão)" => CompressionMethod.Lzma,
            "Zstd (moderno)"           => CompressionMethod.Zstd,
            "NRV2B (mais rápido)"      => CompressionMethod.Nrv2bLE32,
            "NRV2D (equilibrado)"      => CompressionMethod.Nrv2dLE32,
            _                          => CompressionMethod.Nrv2eLE32,
        },
        Level  = Level,
        Backup = Backup,
        Force  = Force ? 1 : 0,
        Brute  = Brute,
    };
}
```

### ProgressViewModel.cs

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

public partial class ProgressViewModel : ObservableObject
{
    public ObservableCollection<LogEntry> LogEntries { get; } = [];

    [ObservableProperty] private bool   _isProcessing;
    [ObservableProperty] private string _currentFileName = string.Empty;
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private string _speed           = string.Empty;

    public void UpdateCurrentFile(string name, double pct)
    {
        CurrentFileName = name;
        ProgressPercent = pct;
    }

    public void AddLog(string message, LogLevel level)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(
            () => LogEntries.Add(new LogEntry(message, level)));
    }

    public void Clear()
    {
        LogEntries.Clear();
        ProgressPercent = 0;
        CurrentFileName = string.Empty;
    }
}

public record LogEntry(string Message, LogLevel Level);
public enum LogLevel { Info, Success, Warning, Error }
```

---

## 11. Commands e Behaviors

### AsyncRelayCommand.cs (alternativa manual ao CommunityToolkit)

```csharp
namespace UpxSharp.Wpf.Commands;

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<CancellationToken, Task> _execute;
    private readonly Func<bool>?                   _canExecute;
    private CancellationTokenSource? _cts;
    private bool _isExecuting;

    public event EventHandler? CanExecuteChanged;

    public AsyncRelayCommand(Func<CancellationToken, Task> execute, Func<bool>? canExecute = null)
    {
        _execute    = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? _) => !_isExecuting && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? _)
    {
        if (!CanExecute(null)) return;
        _cts         = new CancellationTokenSource();
        _isExecuting = true;
        RaiseCanExecuteChanged();
        try     { await _execute(_cts.Token); }
        finally
        {
            _isExecuting = false;
            _cts.Dispose();
            _cts = null;
            RaiseCanExecuteChanged();
        }
    }

    public void Cancel() => _cts?.Cancel();
    private void RaiseCanExecuteChanged()
        => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
```

### AutoScrollBehavior.cs

```csharp
namespace UpxSharp.Wpf.Behaviors;

public static class AutoScrollBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled", typeof(bool), typeof(AutoScrollBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(DependencyObject d, bool v) => d.SetValue(IsEnabledProperty, v);
    public static bool GetIsEnabled(DependencyObject d)         => (bool)d.GetValue(IsEnabledProperty);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer sv) return;
        if ((bool)e.NewValue) sv.ScrollChanged += OnScrollChanged;
        else                  sv.ScrollChanged -= OnScrollChanged;
    }

    private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange > 0 && sender is ScrollViewer sv)
            sv.ScrollToEnd();
    }
}
```

---

## 12. Converters

```csharp
// Converters/StatusToIconConverter.cs
using System.Globalization;
using System.Windows.Data;

namespace UpxSharp.Wpf.Converters;

[ValueConversion(typeof(FileProcessingStatus), typeof(string))]
public sealed class StatusToIconConverter : IValueConverter
{
    public object Convert(object value, Type _, object __, CultureInfo ___)
        => value is FileProcessingStatus s ? s switch
        {
            FileProcessingStatus.Pending    => "○",
            FileProcessingStatus.Processing => "⏳",
            FileProcessingStatus.Done       => "✔",
            FileProcessingStatus.Error      => "✘",
            FileProcessingStatus.Skipped    => "⊘",
            _                               => "?"
        } : "?";

    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}

// Converters/StatusToColorConverter.cs
[ValueConversion(typeof(FileProcessingStatus), typeof(Brush))]
public sealed class StatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type _, object __, CultureInfo ___)
    {
        var key = value is FileProcessingStatus s ? s switch
        {
            FileProcessingStatus.Done       => "Brush.Status.Success",
            FileProcessingStatus.Error      => "Brush.Status.Error",
            FileProcessingStatus.Processing => "Brush.Status.Processing",
            FileProcessingStatus.Skipped    => "Brush.Text.Muted",
            _                               => "Brush.Text.Secondary"
        } : "Brush.Text.Secondary";

        return Application.Current.Resources[key] as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}

// Converters/BoolToVisibilityConverter.cs
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type _, object __, CultureInfo ___)
    {
        bool b = value is bool b2 && b2;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}
```

---

## 13. Integração com UpxSharp Core

### IFileProcessingService.cs

```csharp
namespace UpxSharp.Wpf.Services;

public interface IFileProcessingService
{
    Task ProcessAsync(
        IEnumerable<FileItemViewModel> files,
        Options options,
        Action<FileItemViewModel, double> onProgress,
        Action<string, LogLevel> onLog,
        CancellationToken ct);
}
```

### FileProcessingService.cs

```csharp
public sealed class FileProcessingService : IFileProcessingService
{
    public async Task ProcessAsync(
        IEnumerable<FileItemViewModel> files,
        Options options,
        Action<FileItemViewModel, double> onProgress,
        Action<string, LogLevel> onLog,
        CancellationToken ct)
    {
        foreach (var vm in files)
        {
            ct.ThrowIfCancellationRequested();

            vm.Status   = FileProcessingStatus.Processing;
            vm.Progress = 0;

            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                await Task.Run(() =>
                {
                    using var input  = new InputFile(vm.FilePath);
                    var outPath      = options.OutputFile ?? vm.FilePath;
                    using var output = new OutputFile(outPath, overwrite: true);

                    var packer = options.Command == UpxCommand.Decompress
                        ? PackMaster.FindUnpacker(input, options)
                        : PackMaster.FindPacker(input, options);

                    onProgress(vm, 20);
                    if (options.Command == UpxCommand.Decompress)
                        packer.DoUnpack(output, options);
                    else
                        packer.DoPack(output, options);

                    onProgress(vm, 100);
                }, ct);

                sw.Stop();
                vm.CompressedSize = new FileInfo(vm.FilePath).Length;
                vm.Status         = FileProcessingStatus.Done;

                onLog(
                    $"✔ {vm.FileName}: {vm.OriginalSizeFormatted} → " +
                    $"{vm.CompressedSizeFormatted} ({vm.RatioFormatted}) em " +
                    $"{sw.Elapsed.TotalSeconds:F1}s",
                    LogLevel.Success);
            }
            catch (OperationCanceledException)
            {
                vm.Status = FileProcessingStatus.Skipped;
                onLog($"⊘ {vm.FileName}: cancelado pelo usuário", LogLevel.Warning);
                throw;
            }
            catch (UpxException ex)
            {
                vm.Status       = FileProcessingStatus.Error;
                vm.ErrorMessage = ex.Message;
                onLog($"✘ {vm.FileName}: {ex.Message}", LogLevel.Error);
            }
        }
    }
}

// Services/IDialogService.cs
public interface IDialogService
{
    string[]? OpenFiles(string title, string filter);
    string?   OpenFolder(string title);
}

// Services/DialogService.cs
public sealed class DialogService : IDialogService
{
    public string[]? OpenFiles(string title, string filter)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title           = title,
            Filter          = filter,
            Multiselect     = true,
            CheckFileExists = true,
        };
        return dlg.ShowDialog() == true ? dlg.FileNames : null;
    }

    public string? OpenFolder(string title)
    {
        // No .NET 10 / WPF: usar OpenFolderDialog
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = title };
        return dlg.ShowDialog() == true ? dlg.FolderName : null;
    }
}
```

---

## 14. Checklist de Implementação Visual

### Fase 1 — Setup do Projeto
- [x] Criar projeto `UpxSharp.Wpf` (`net10.0-windows`, `UseWPF`)
- [x] Adicionar referência ao `UpxSharp` (core)
- [x] Instalar `CommunityToolkit.Mvvm`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Xaml.Behaviors.Wpf`
- [x] Configurar DI em `App.xaml.cs`
- [x] Criar `Colors.xaml`, `Typography.xaml`, `Controls.xaml`
- [x] Registrar ResourceDictionaries em `App.xaml`
- [x] Registrar converters globais em `App.xaml`

### Fase 2 — Janela Principal
- [x] `MainWindow.xaml` com `WindowStyle="None"` e `WindowChrome` customizado
- [x] TitleBar com arrastar, minimizar, maximizar, fechar
- [x] Grid principal: TitleBar + ToolBar + Conteúdo + ProgressPanel
- [x] ToolBar com botões Comprimir, Descomprimir, Testar, Limpar

### Fase 3 — Controles
- [x] `FileDropPanel.xaml` com área de drop e feedback visual de drag
- [x] `FileDropPanel.xaml.cs` capturando `Drop`, `DragOver`, `DragEnter`, `DragLeave`
- [x] `FileListView.xaml` com DataGrid: ícone de status, nome, tamanho, redução, progresso inline, botão remover
- [x] `OptionsPanel.xaml` com os campos suportados pelo core (método, nível, filtro automático, backup, força, saída)
- [x] `ProgressPanel.xaml` com barra global e log em `ItemsControl`

### Fase 4 — ViewModels e Commands
- [x] `FileItemViewModel` com `ObservableProperty` e propriedades calculadas
- [x] `MainViewModel` com `Files`, `State`, `IsDragOver` e todos os `RelayCommand`
- [x] `OptionsViewModel` com `ToOptions()` mapeando para `Options` do core
- [x] `ProgressViewModel` com `LogEntries`, `IsProcessing`, `UpdateCurrentFile`, `AddLog`

### Fase 5 — Converters e Behaviors
- [x] `StatusToIconConverter` → string de ícone por status
- [x] `StatusToColorConverter` → `Brush` por status
- [x] `BoolToVisibilityConverter` com propriedade `Invert`
- [x] `AutoScrollBehavior` (attached property em `ScrollViewer`)

### Fase 6 — Integração e Serviços
- [x] `IFileProcessingService` / `FileProcessingService` chamando `PackMaster`
- [x] `IDialogService` / `DialogService` com `OpenFileDialog` e `OpenFolderDialog`
- [x] Callback de progresso atualizando `FileItemViewModel.Progress`
- [x] Log de resultado (tamanho, redução, tempo) em `ProgressViewModel`
- [x] Tratamento de `UpxException` e `OperationCanceledException`

### Fase 7 — Polimento
- [x] Atalhos de teclado: `Ctrl+O` = abrir arquivos, `F5` = comprimir, `Esc` = cancelar
- [x] `ToolTip` nos botões e controles
- [x] Ícone `.ico` em múltiplas resoluções (16, 32, 48, 256px)
- [x] Transição animada de altura no `ProgressPanel` (colapsa quando vazio)
- [x] Feedback visual de `DragOver` com borda pulsante ou cor de destaque
- [x] Menu de contexto no DataGrid (Remover, Abrir pasta, Copiar caminho)
- [x] Salvar dimensões e posição da janela entre sessões (`Settings`)

---

*Documento gerado em 15/06/2026 — WPF / .NET 10 / C# 13 / MVVM com CommunityToolkit.Mvvm*
