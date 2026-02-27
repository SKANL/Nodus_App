using System.Globalization;

namespace Nodus.Client.Converters;

/// <summary>
/// Inverts booleans and truthy values for XAML visibility bindings.
/// Handles bool, int (0 = true), and double (0 = true).
/// </summary>
public class InvertedBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            bool b => !b,
            int i => i == 0,
            double d => d == 0,
            _ => true
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            bool b => !b,
            _ => false
        };
    }
}

/// <summary>
/// Returns a Color depending on a boolean.
/// Defaults: True → #EF4444 (red/busy), False → #22C55E (green/ready).
/// Override TrueColor/FalseColor as string hex values.
/// </summary>
public class BoolToColorConverter : IValueConverter
{
    public string TrueColor { get; set; } = "#EF4444";
    public string FalseColor { get; set; } = "#22C55E";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hex = (value is bool b && b) ? TrueColor : FalseColor;
        return Color.FromArgb(hex);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Picks between two strings based on a boolean.
/// ConverterParameter format: "TrueText|FalseText"  e.g. "Enviando...|Votar ✓"
/// </summary>
public class BoolToTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var parts = parameter?.ToString()?.Split('|');
        if (parts is null || parts.Length < 2) return parameter?.ToString() ?? "";
        return (value is bool b && b) ? parts[0] : parts[1];
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
