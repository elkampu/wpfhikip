using System;
using System.Globalization;
using System.Windows.Data;

using wpfhikip.Models;

namespace wpfhikip.Converters
{
    public class EnumToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is CameraProtocol protocol)
            {
                return protocol.ToString();
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string stringValue && Enum.TryParse<CameraProtocol>(stringValue, out var protocol))
            {
                return protocol;
            }
            return CameraProtocol.Auto;
        }
    }
}