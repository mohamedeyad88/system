using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Apex.UI.Models;

namespace Apex.UI.Converters
{
    public class GuideLineX1Converter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2 || values[0] is not GuideLine guide || values[1] is not double canvasWidth)
                return 0.0;

            // Position is in pixels, use directly
            if (guide.Orientation == Orientation.Vertical)
                return guide.Position;
            return 0.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            return new object[] { Binding.DoNothing };
        }
    }

    public class GuideLineY1Converter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2 || values[0] is not GuideLine guide || values[1] is not double canvasHeight)
                return 0.0;

            // Position is in pixels, use directly
            if (guide.Orientation == Orientation.Horizontal)
                return guide.Position;
            return 0.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            return new object[] { Binding.DoNothing };
        }
    }

    public class GuideLineX2Converter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2 || values[0] is not GuideLine guide || values[1] is not double canvasWidth)
                return 0.0;

            // Position is in pixels, use directly
            if (guide.Orientation == Orientation.Vertical)
                return guide.Position;
            return canvasWidth;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            return new object[] { Binding.DoNothing };
        }
    }

    public class GuideLineY2Converter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2 || values[0] is not GuideLine guide || values[1] is not double canvasHeight)
                return 0.0;

            // Position is in pixels, use directly
            if (guide.Orientation == Orientation.Horizontal)
                return guide.Position;
            return canvasHeight;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            return new object[] { Binding.DoNothing };
        }
    }
}
