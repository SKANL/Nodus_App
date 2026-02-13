using Microsoft.Maui.Graphics;

namespace Nodus.Server.Converters;

public class QualityToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is double quality)
        {
            if (quality >= 70) return Colors.Green;
            if (quality >= 40) return Colors.Orange;
            return Colors.Red;
        }
        return Colors.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class PacketLossToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is double packetLoss)
        {
            if (packetLoss < 5) return Colors.Green;
            if (packetLoss < 15) return Colors.Orange;
            return Colors.Red;
        }
        return Colors.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
