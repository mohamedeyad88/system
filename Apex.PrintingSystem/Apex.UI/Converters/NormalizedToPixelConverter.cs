using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Apex.UI.Converters
{
    public class NormalizedToPixelConverter : IMultiValueConverter, IValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (values == null || values.Length < 2) 
                    return 0.0;
                
                if (values[0] == DependencyProperty.UnsetValue || values[1] == DependencyProperty.UnsetValue)
                    return 0.0;
                
                double normalized = 0;
                double totalSize = 0;
                
                // Handle different numeric types for normalized value
                if (values[0] is float f) normalized = f;
                else if (values[0] is double d) normalized = d;
                else if (values[0] is int i) normalized = i;
                else return 0.0;
                
                // Handle totalSize
                if (values[1] is double ts) totalSize = ts;
                else if (values[1] is float fs) totalSize = fs;
                else if (values[1] is int its) totalSize = its;
                else return 0.0;
                
                // Clamp to prevent negative or excessively large values
                var result = normalized * totalSize;
                return Math.Max(0, Math.Min(result, totalSize));
            }
            catch
            {
                return 0.0;
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return 0.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

