using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Apex.UI.Converters
{
    public class BooleanToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Defensive: handle null or binding errors
            if (value == null || value == System.Windows.DependencyProperty.UnsetValue)
                return new SolidColorBrush(Colors.Transparent);

            if (value is bool isOnline && isOnline)
            {
                return new SolidColorBrush(Colors.Green);
            }
            return new SolidColorBrush(Colors.Red);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
