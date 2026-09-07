using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Apex.UI.Converters
{
    /// <summary>
    /// Converts a slot's stored <c>FontSize</c> into the pixel size it will actually occupy
    /// on the design canvas, so the designer shows the number at the size it prints.
    ///
    /// <para><b>Why this exists.</b> <c>SlotSpec.FontSize</c> is stored in design units at
    /// 96&#160;dpi — that is the contract every saved project and .apext file on a customer's
    /// machine was written against. The print engine scales it up by the template's own
    /// resolution (<c>Composer.ComputeDpiScale</c>), so on a 300&#160;dpi A4 scan a stored 24
    /// prints at 75&#160;px. The designer, however, bound the raw 24 straight to a TextBlock on
    /// a canvas laid out at the template's pixel size — so it drew the number at a THIRD of
    /// its printed size, and on a high-resolution design that is a speck a few pixels tall.
    /// That is what operators reported as "I add a numbering field and nothing shows up":
    /// the field was there and printed correctly, it was just drawn far too small to see.</para>
    ///
    /// <para>The multiplier below must stay identical to
    /// <c>Composer.ComputeDpiScale</c> — same A4 width, same 96&#160;dpi design basis, same
    /// 1..5 clamp. If one moves, the designer stops matching the press again.</para>
    /// </summary>
    public class SlotFontScaleConverter : IMultiValueConverter
    {
        private const double A4WidthInches = 8.27;
        private const double DesignDpi = 96.0;

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            const double fallback = 12.0;
            if (values == null || values.Length < 2) return fallback;
            if (values[0] == DependencyProperty.UnsetValue || values[1] == DependencyProperty.UnsetValue)
                return fallback;

            double fontSize = ToDouble(values[0]);
            double canvasWidth = ToDouble(values[1]);
            if (fontSize <= 0) return fallback;

            double scale = 1.0;
            if (canvasWidth > 0)
                scale = Math.Clamp(canvasWidth / (A4WidthInches * DesignDpi), 1.0, 5.0);

            // WPF throws on a non-positive FontSize, so never return zero.
            return Math.Max(1.0, fontSize * scale);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => new object[] { Binding.DoNothing, Binding.DoNothing };

        private static double ToDouble(object v) => v switch
        {
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            _ => 0.0
        };
    }
}
