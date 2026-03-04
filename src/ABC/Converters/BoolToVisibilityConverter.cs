using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ABC.Converters;

[ValueConversion(typeof(bool), typeof(Visibility))]
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool boolValue = value is bool b && b;
        bool invert = parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase);
        if (invert) boolValue = !boolValue;
        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool result = value is Visibility v && v == Visibility.Visible;
        bool invert = parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase);
        return invert ? !result : result;
    }
}
