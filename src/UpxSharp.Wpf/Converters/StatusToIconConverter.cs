using System.Globalization;
using System.Windows.Data;
using UpxSharp.Wpf.ViewModels;

namespace UpxSharp.Wpf.Converters;

[ValueConversion(typeof(FileProcessingStatus), typeof(string))]
public sealed class StatusToIconConverter : IValueConverter
{
    public object Convert(object value, Type _, object __, CultureInfo ___)
        => value is FileProcessingStatus status ? status switch
        {
            FileProcessingStatus.Pending => "○",
            FileProcessingStatus.Processing => "◌",
            FileProcessingStatus.Done => "✔",
            FileProcessingStatus.Error => "✘",
            FileProcessingStatus.Skipped => "⊘",
            _ => "?"
        } : "?";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
