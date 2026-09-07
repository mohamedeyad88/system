using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Apex.UI.Converters
{
    /// <summary>
    /// Turns a zero-based item index into a "صفحة ١" caption for the multi-page preview.
    ///
    /// The caption used to be bound to the ContentPresenter itself, so every thumbnail
    /// was labelled "System.Windows.Controls.ContentPresenter" — four identical pages
    /// with no way to tell which sheet was which.
    /// </summary>
    public class PageIndexToLabelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var index = value switch
            {
                int i => i,
                long l => (int)l,
                _ => 0
            };

            var format = Application.Current?.TryFindResource("Num_PreviewPageLabel") as string ?? "Page {0}";
            return string.Format(culture, format, index + 1);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
