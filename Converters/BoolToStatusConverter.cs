using System.Globalization;
using System.Windows.Data;

namespace wpfhikip.Converters
{
    /// <summary>
    /// Converts boolean scanning state to status text
    /// </summary>
    public class BoolToStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isScanning)
            {
                return isScanning ? "Scanning..." : "Ready";
            }
            return "Ready";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}