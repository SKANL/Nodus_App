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
