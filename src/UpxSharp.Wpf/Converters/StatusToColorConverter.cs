using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using UpxSharp.Wpf.ViewModels;

namespace UpxSharp.Wpf.Converters;

[ValueConversion(typeof(FileProcessingStatus), typeof(Brush))]
public sealed class StatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type _, object __, CultureInfo ___)
    {
        string key = value is FileProcessingStatus status ? status switch
        {
            FileProcessingStatus.Done => "Brush.Status.Success",
            FileProcessingStatus.Error => "Brush.Status.Error",
            FileProcessingStatus.Processing => "Brush.Status.Processing",
            FileProcessingStatus.Skipped => "Brush.Text.Muted",
            _ => "Brush.Text.Secondary"
        } : "Brush.Text.Secondary";

        return Application.Current.Resources[key] as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
