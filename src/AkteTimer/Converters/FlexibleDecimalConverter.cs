using System;
using System.Globalization;
using System.Windows.Data;

namespace AkteTimer.Converters;

public sealed class FlexibleDecimalConverter : IValueConverter
{
    public string Format { get; set; } = "N2";

    public string? Suffix { get; set; }

    public bool AllowNull { get; set; }

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null)
        {
            return string.Empty;
        }

        if (value is string text)
        {
            return text;
        }

        if (value is IConvertible convertible)
        {
            var decimalValue = System.Convert.ToDecimal(convertible, CultureInfo.InvariantCulture);
            var formatted = decimalValue.ToString(Format, culture);
            if (!string.IsNullOrWhiteSpace(Suffix))
            {
                formatted = $"{formatted} {Suffix}";
            }

            return formatted;
        }

        return value.ToString() ?? string.Empty;
    }

    public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return AllowNull ? null : 0m;
        }

        if (!string.IsNullOrWhiteSpace(Suffix))
        {
            text = text.Replace(Suffix, string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        text = text.Replace("€", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        var containsComma = text.Contains(',');
        var containsDot = text.Contains('.');

        if (containsDot && !containsComma && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        if (decimal.TryParse(text, NumberStyles.Number, culture, out parsed))
        {
            return parsed;
        }

        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed))
        {
            return parsed;
        }

        var normalized = text.Replace(',', '.');
        if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed))
        {
            return parsed;
        }

        return System.Windows.Data.Binding.DoNothing;
    }
}
