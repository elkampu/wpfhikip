using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace wpfhikip.Converters
{
    /// <summary>
    /// Converter that converts boolean to Visibility with inversion support
    /// </summary>
    public class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isVisible = false;

            if (value is bool boolValue)
            {
                isVisible = boolValue;
            }

            // Check if inversion is requested
            if (parameter?.ToString()?.Equals("Invert", StringComparison.OrdinalIgnoreCase) == true)
            {
                isVisible = !isVisible;
            }

            return isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                bool isVisible = visibility == Visibility.Visible;

                // Check if inversion is requested
                if (parameter?.ToString()?.Equals("Invert", StringComparison.OrdinalIgnoreCase) == true)
                {
                    isVisible = !isVisible;
                }

                return isVisible;
            }
            return false;
        }
    }
}