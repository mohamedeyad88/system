using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Apex.UI.Converters
{
    /// <summary>
    /// Inverts a boolean — and returns a <see cref="Visibility"/> when that is what
    /// the binding target expects.
    ///
    /// Five bindings across the app used this converter on a Visibility property.
    /// It handed back a bool, WPF could not use it, and the element simply never
    /// collapsed: the Imposition screen showed "no file chosen yet" under a loaded
    /// file's name, and the Numbered Books print step drew its header on top of the
    /// prepare step's, producing the unreadable overlap the shop owner reported.
    ///
    /// Honouring targetType fixes every one of those call sites without touching a
    /// line of XAML, and keeps the plain boolean behaviour for the bindings that
    /// genuinely want a bool.
    /// </summary>
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => Shape(value is bool b ? !b : false, targetType);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility v) return v != Visibility.Visible;
            return value is bool b ? !b : false;
        }

        private static object Shape(bool result, Type targetType)
        {
            if (targetType == typeof(Visibility) || targetType == typeof(Visibility?))
                return result ? Visibility.Visible : Visibility.Collapsed;

            return result;
        }
    }
}
