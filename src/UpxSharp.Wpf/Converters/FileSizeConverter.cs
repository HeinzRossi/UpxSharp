using System.Globalization;
using System.Windows.Data;

namespace UpxSharp.Wpf.Converters
{
    /// <summary>
    /// Converte um valor numérico de bytes para uma string legível por humanos.
    /// Suporta <see cref="long"/>, <see cref="int"/>, <see cref="ulong"/> e <see cref="double"/>.
    ///
    /// Exemplos:
    ///   512         → "512 B"
    ///   1 536       → "1.5 KB"
    ///   1 048 576   → "1.0 MB"
    ///   2 147 483   → "2.0 MB"
    ///
    /// O parâmetro opcional controla o número de casas decimais (padrão: 1).
    /// Uso: Converter={StaticResource FileSizeConverter}, ConverterParameter=2
    /// </summary>
    [ValueConversion(typeof(long), typeof(string))]
    public sealed class FileSizeConverter : IValueConverter
    {
        private const long KB = 1024L;
        private const long MB = 1024L * 1024;
        private const long GB = 1024L * 1024 * 1024;
        private const long TB = 1024L * 1024 * 1024 * 1024;

        /// <summary>
        /// Número padrão de casas decimais quando nenhum parâmetro for informado.
        /// </summary>
        public int DefaultDecimals { get; set; } = 1;

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            long bytes = value switch
            {
                long   l => l,
                int    i => i,
                uint   u => u,
                ulong  u => (long)u,
                double d => (long)d,
                float  f => (long)f,
                _        => -1
            };

            if (bytes < 0) return "—";
            if (bytes == 0) return "0 B";

            int decimals = parameter is string s && int.TryParse(s, out int d2) ? d2 : DefaultDecimals;
            string fmt   = $"F{decimals}";

            return bytes switch
            {
                < KB => $"{bytes} B",
                < MB => $"{bytes / (double)KB:fmt} KB",
                < GB => $"{bytes / (double)MB:fmt} MB",
                < TB => $"{bytes / (double)GB:fmt} GB",
                _    => $"{bytes / (double)TB:fmt} TB",
            };
        }

        /// <summary>ConvertBack não é suportado — tamanho é somente leitura na UI.</summary>
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException($"{nameof(FileSizeConverter)} não suporta ConvertBack.");
    }

    /// <summary>
    /// Converte uma razão de compressão (0.0–1.0) para string percentual de redução.
    /// Ex: 0.6 → "60.0% menor"
    ///     0.0 → "—"    (ainda não comprimido)
    ///    -0.1 → "+10.0% maior"  (arquivo ficou maior após compressão)
    /// </summary>
    [ValueConversion(typeof(double), typeof(string))]
    public sealed class CompressionRatioConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not double ratio) return "—";
            if (ratio == 0.0)             return "—";

            double pct = ratio * 100.0;

            return pct switch
            {
                > 0  => $"{pct:F1}% menor",
                < 0  => $"+{Math.Abs(pct):F1}% maior",
                _    => "—"
            };
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}