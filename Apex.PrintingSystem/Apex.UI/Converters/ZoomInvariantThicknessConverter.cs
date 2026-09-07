using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Apex.UI.Converters
{
    /// <summary>
    /// Returns a <see cref="Thickness"/> that keeps its on-screen width constant while the
    /// design canvas is scaled by the zoom <c>LayoutTransform</c>.
    ///
    /// The numbering canvas is laid out at the design image's own pixel size — a 300&#160;dpi A4
    /// scan is ~2480&#215;3508 — and zoomed to fit, so a fit view runs at roughly 0.25&#215;. A plain
    /// 1&#160;px outline on a slot would render at a quarter of a device pixel there and vanish,
    /// which is exactly how an operator ends up believing an added field "did not appear".
    /// Dividing by the zoom cancels the transform out.
    /// </summary>
    public class ZoomInvariantThicknessConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double zoom = value switch
            {
                double d => d,
                float f => f,
                int i => i,
                _ => 1.0
            };

            if (double.IsNaN(zoom) || double.IsInfinity(zoom) || zoom <= 0.01)
                zoom = 1.0;

            double basePx = 1.5;
            if (parameter != null &&
                double.TryParse(System.Convert.ToString(parameter, CultureInfo.InvariantCulture),
                                NumberStyles.Float, CultureInfo.InvariantCulture, out var p) && p > 0)
            {
                basePx = p;
            }

            return new Thickness(basePx / zoom);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
