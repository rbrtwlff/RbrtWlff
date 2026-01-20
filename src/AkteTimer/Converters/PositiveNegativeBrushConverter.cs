using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AkteTimer.Converters;

public sealed class PositiveNegativeBrushConverter : IValueConverter
{
    public Brush PositiveBrush { get; set; } = Brushes.ForestGreen;
    public Brush NegativeBrush { get; set; } = Brushes.Firebrick;
    public Brush ZeroBrush { get; set; } = Brushes.Black;

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
        => Binding.DoNothing;
}
