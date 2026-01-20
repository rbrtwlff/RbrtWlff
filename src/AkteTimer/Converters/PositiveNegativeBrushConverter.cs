using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AkteTimer.Converters;

public sealed class PositiveNegativeBrushConverter : IValueConverter
{
    public System.Windows.Media.Brush PositiveBrush { get; set; } = System.Windows.Media.Brushes.ForestGreen;
    public System.Windows.Media.Brush NegativeBrush { get; set; } = System.Windows.Media.Brushes.Firebrick;
    public System.Windows.Media.Brush ZeroBrush { get; set; } = System.Windows.Media.Brushes.Black;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is decimal decimalValue)
        {
            if (decimalValue > 0m)
            {
                return PositiveBrush;
            }

            if (decimalValue < 0m)
            {
                return NegativeBrush;
            }
        }

        return ZeroBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => System.Windows.Data.Binding.DoNothing;
}
