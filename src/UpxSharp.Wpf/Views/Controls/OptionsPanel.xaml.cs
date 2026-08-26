using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UpxSharp.Wpf.ViewModels;

namespace UpxSharp.Wpf.Views.Controls;

public partial class OptionsPanel : UserControl
{
    private static readonly string[] LevelDescriptions =
    [
        "",
        "Nível 1: compressão mais rápida",
        "Nível 2: rápido",
        "Nível 3: rápido com ganho moderado",
        "Nível 4: equilibrado",
        "Nível 5: equilíbrio recomendado",
        "Nível 6: boa compressão",
        "Nível 7: alta compressão",
        "Nível 8: alta compressão, padrão UpxSharp",
        "Nível 9: máxima compressão, mais lento",
    ];

    public OptionsPanel() => InitializeComponent();

    private void LevelSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is Slider slider)
        {
            int level = Math.Clamp((int)e.NewValue, 1, 9);
            slider.ToolTip = LevelDescriptions[level];
        }
    }

    private void OutputPathTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is not OptionsViewModel viewModel ||
            !viewModel.IsCustomOutputPath)
        {
            return;
        }

        string path = viewModel.CustomOutputPath.Trim();
        if (Directory.Exists(path))
            viewModel.CustomOutputPath = Path.GetFullPath(path);
    }

    private void OutputPathTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        Keyboard.ClearFocus();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Escape &&
            DataContext is OptionsViewModel { IsCustomOutputPath: true } viewModel)
        {
            viewModel.CustomOutputPath = string.Empty;
            e.Handled = true;
        }
    }
}
