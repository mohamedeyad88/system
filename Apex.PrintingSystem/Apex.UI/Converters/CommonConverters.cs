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
            return Binding.DoNothing;
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
            return Binding.DoNothing;
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
            return Binding.DoNothing;
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
            return Binding.DoNothing;
        }
    }

    /// <summary>Returns Visible when the string is NOT empty; Collapsed when empty/null.</summary>
    public class NonEmptyStringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
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
            return Binding.DoNothing;
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
            return Binding.DoNothing;
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
            return Binding.DoNothing;
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
            return Binding.DoNothing;
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
            return Binding.DoNothing;
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
            return Binding.DoNothing;
        }
    }

    /// <summary>
    /// Converts a numeric value greater than zero to Visibility.Visible, otherwise Collapsed.
    /// </summary>
    public class GreaterThanZeroToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is decimal d)
                return d > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (value is double db)
                return db > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (value is int i)
                return i > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (value is float f)
                return f > 0 ? Visibility.Visible : Visibility.Collapsed;
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    /// <summary>
    /// Converts DensityMode string to bool (Compact = true, Comfortable = false)
    /// </summary>
    public class DensityModeToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string mode)
                return mode == "Compact";
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    /// <summary>
    /// Converts window width to number of columns for responsive grid layout.
    /// Returns 4 for large screens (>1200px), 3 for medium (800-1200px), 2 for small (<800px)
    /// </summary>
    public class WidthToColumnsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double width)
            {
                if (width > 1200)
                    return 4;
                else if (width > 800)
                    return 3;
                else
                    return 2;
            }
            return 4; // Default
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    /// <summary>
    /// Converts bool to status background color (true = green, false = yellow)
    /// </summary>
    public class BoolToStatusColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || value == DependencyProperty.UnsetValue)
                return new SolidColorBrush(Color.FromRgb(0xFE, 0xF3, 0xC7)); // Yellow fallback
                
            if (value is bool b && b)
                return new SolidColorBrush(Color.FromRgb(0xD1, 0xFA, 0xE5)); // Green
            return new SolidColorBrush(Color.FromRgb(0xFE, 0xF3, 0xC7)); // Yellow
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    /// <summary>
    /// Converts bool to status border color (true = green, false = yellow)
    /// </summary>
    public class BoolToStatusBorderConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || value == DependencyProperty.UnsetValue)
                return new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)); // Yellow border fallback
                
            if (value is bool b && b)
                return new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)); // Green border
            return new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)); // Yellow border
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    /// <summary>
    /// Converts bool to status text color (true = dark green, false = dark yellow)
    /// </summary>
    public class BoolToStatusTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || value == DependencyProperty.UnsetValue)
                return new SolidColorBrush(Color.FromRgb(0x92, 0x40, 0x0E)); // Dark yellow fallback
                
            if (value is bool b && b)
                return new SolidColorBrush(Color.FromRgb(0x06, 0x5F, 0x46)); // Dark green
            return new SolidColorBrush(Color.FromRgb(0x92, 0x40, 0x0E)); // Dark yellow
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    /// <summary>
    /// Converts enum value to bool by comparing with parameter string.
    /// Used for RadioButton binding with enum values.
    /// </summary>
    public class EnumToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return false;

            string parameterString = parameter.ToString() ?? "";
            if (string.IsNullOrEmpty(parameterString))
                return false;

            if (Enum.IsDefined(value.GetType(), value))
            {
                object parameterValue = Enum.Parse(value.GetType(), parameterString);
                return value.Equals(parameterValue);
            }

            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return null;

            if ((bool)value)
            {
                string parameterString = parameter.ToString() ?? "";
                if (Enum.IsDefined(targetType, parameterString))
                {
                    return Enum.Parse(targetType, parameterString);
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Converts between long and string for TextBox binding (TwoWay).
    /// Handles conversion errors gracefully.
    /// </summary>
    public class LongToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Convert long to string for display
            if (value is long longValue)
                return longValue.ToString();
            if (value is int intValue)
                return intValue.ToString();
            return "0";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Convert string to long for binding back
            if (value is string stringValue)
            {
                if (string.IsNullOrWhiteSpace(stringValue))
                    return 0L;
                
                if (long.TryParse(stringValue, out long result))
                    return result;
            }
            
            // Return 0 if conversion fails
            return 0L;
        }
    }

    /// <summary>
    /// Converts WorkflowMode enum to bool for mode visibility
    /// </summary>
    public class WorkflowModeToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return false;

            if (value is Apex.UI.ViewModels.WorkflowMode currentMode)
            {
                string parameterString = parameter.ToString() ?? "";
                if (Enum.TryParse<Apex.UI.ViewModels.WorkflowMode>(parameterString, out var targetMode))
                {
                    return currentMode == targetMode;
                }
            }

            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    /// <summary>
    /// Extracts only the file name (without extension) from a full file path string.
    /// </summary>
    public class FileNameOnlyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string path && !string.IsNullOrEmpty(path))
                return System.IO.Path.GetFileNameWithoutExtension(path);
            return value ?? "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>
    /// Analytics bar chart: scales a decimal Revenue value to a pixel height.
    /// ConverterParameter = "maxRevenue" (decimal string). Output is clamped to [4, 120].
    /// Usage: Height="{Binding TotalRevenue, Converter={...}, ConverterParameter=500}"
    /// </summary>
    public class RevenueToBarHeightConverter : IValueConverter
    {
        private const double MinHeight = 4.0;
        private const double MaxHeight = 120.0;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double revenue = 0;
            if (value is decimal d) revenue = (double)d;
            else if (value is double db) revenue = db;
            else if (value is int i) revenue = i;

            double maxRevenue = 500.0;
            if (parameter != null && double.TryParse(parameter.ToString(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double pv) && pv > 0)
                maxRevenue = pv;

            if (revenue <= 0) return MinHeight;
            double scaled = (revenue / maxRevenue) * MaxHeight;
            return Math.Max(MinHeight, Math.Min(MaxHeight, scaled));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>
    /// Analytics bar chart: returns a green brush for today's date, blue for all other dates.
    /// </summary>
    public class DateToBarColorConverter : IValueConverter
    {
        private static readonly SolidColorBrush TodayBrush =
            new(Color.FromRgb(0x22, 0xC5, 0x5E));   // #22C55E
        private static readonly SolidColorBrush OtherBrush =
            new(Color.FromRgb(0x3B, 0x82, 0xF6));   // #3B82F6

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DateTime dt && dt.Date == DateTime.Today)
                return TodayBrush;
            return OtherBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}


