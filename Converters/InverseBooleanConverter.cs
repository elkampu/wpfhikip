using System.Globalization;
using System.Windows.Data;

namespace wpfhikip.Converters
{
    /// <summary>
    /// Converts a boolean value to its inverse
    /// </summary>
    public class InverseBooleanConverter : IValueConverter
    {
        public static InverseBooleanConverter Instance { get; } = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
                return !boolValue;

            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
                return !boolValue;

            return value;
        }
    }
}