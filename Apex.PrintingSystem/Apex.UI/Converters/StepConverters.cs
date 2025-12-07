using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Apex.UI.Converters
{
    public class StepVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int currentStep && int.TryParse(parameter?.ToString(), out int targetStep))
            {
                return currentStep == targetStep ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class StepWeightConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int currentStep && int.TryParse(parameter?.ToString(), out int targetStep))
            {
                return currentStep == targetStep ? FontWeights.Bold : FontWeights.Normal;
            }
            return FontWeights.Normal;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class StepBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int currentStep && int.TryParse(parameter?.ToString(), out int targetStep))
            {
                // If step is completed or current, use blue; otherwise gray
                if (currentStep >= targetStep)
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6"));
                else
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CBD5E1"));
            }
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CBD5E1"));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class BoolToVisConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? Visibility.Visible : Visibility.Collapsed;
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}

