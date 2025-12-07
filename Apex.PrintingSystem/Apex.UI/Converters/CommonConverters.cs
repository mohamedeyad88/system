using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Apex.UI.Converters
{
    public class NullToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value != null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts any value to Visibility: Visible if not null, Collapsed if null
    /// </summary>
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value != null ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class InverseCountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int count && count > 0)
                return Visibility.Collapsed;
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class EmptyStringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class PercentToWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double percent = 0;
            double maxWidth = 100;

            if (value is double d)
                percent = d;
            else if (value is int i)
                percent = i;
            else if (value is float f)
                percent = f;

            if (parameter != null && double.TryParse(parameter.ToString(), out double pValue))
                maxWidth = pValue;

            // Clamp percent to [0, 100]
            percent = Math.Max(0, Math.Min(100, percent));
            return (percent / 100.0) * maxWidth;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class DivideByTwoConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d)
                return d / 2.0;
            if (value is int i)
                return i / 2.0;
            if (value is float f)
                return f / 2.0;
            return 0.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    // Convert bool to FontWeight (for typography)
    public class BoolToFontWeightConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isBold && isBold)
                return FontWeights.Bold;
            return FontWeights.Normal;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    // Convert bool to color (true = accent blue, false = card bg)
    public class BoolToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Defensive: handle null or binding errors
            if (value == null || value == DependencyProperty.UnsetValue)
                return new SolidColorBrush(Color.FromRgb(0x2D, 0x32, 0x3C)); // CardBg fallback
                
            if (value is bool b && b)
                return new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)); // AccentBlue
            return new SolidColorBrush(Color.FromRgb(0x2D, 0x32, 0x3C)); // CardBg
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    // Inverse bool to color (false = accent blue, true = card bg)
    public class InverseBoolToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Defensive: handle null or binding errors
            if (value == null || value == DependencyProperty.UnsetValue)
                return new SolidColorBrush(Color.FromRgb(0x2D, 0x32, 0x3C)); // CardBg fallback
                
            if (value is bool b && !b)
                return new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)); // AccentBlue
            return new SolidColorBrush(Color.FromRgb(0x2D, 0x32, 0x3C)); // CardBg
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts percent (0-100) to opacity (0.0-1.0)
    /// </summary>
    public class PercentToOpacityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double percent = 100;
            
            if (value is int i)
                percent = i;
            else if (value is double d)
                percent = d;
            else if (value is float f)
                percent = f;
            
            // Clamp and convert to 0.0-1.0
            percent = Math.Max(0, Math.Min(100, percent));
            return percent / 100.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double opacity)
                return (int)(opacity * 100);
            return 100;
        }
    }
    /// <summary>
    /// Converts bool to text based on parameter "TrueText|FalseText"
    /// </summary>
    public class BooleanToTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string param = parameter as string ?? "True|False";
            var parts = param.Split('|');
            string trueText = parts.Length > 0 ? parts[0] : "True";
            string falseText = parts.Length > 1 ? parts[1] : "False";

            if (value is bool b && b)
                return trueText;
            return falseText;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}


