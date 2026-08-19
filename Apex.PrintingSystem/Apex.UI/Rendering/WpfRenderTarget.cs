using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Apex.Services.SmartVariables;

namespace Apex.UI.Rendering
{
    /// <summary>
    /// Draws onto a WPF <see cref="DrawingContext"/>. Backs BOTH the live preview
    /// (via <c>TemplatePreviewControl.OnRender</c>) and the raster export (via
    /// <c>RenderTargetBitmap</c>), so what the operator sees is what gets exported.
    /// </summary>
    public sealed class WpfRenderTarget : IRenderTarget
    {
        private readonly DrawingContext _dc;
        private int _pushCount;

        public WpfRenderTarget(DrawingContext dc) => _dc = dc;

        public void FillRect(Rect rect, Brush brush)
        {
            if (brush == null || rect.Width <= 0 || rect.Height <= 0) return;
            _dc.DrawRectangle(brush, null, rect);
        }

        public void StrokeRect(Rect rect, Brush stroke, double thickness)
        {
            if (stroke == null || thickness <= 0) return;
            var pen = new Pen(stroke, thickness);
            pen.Freeze();
            _dc.DrawRectangle(null, pen, rect);
        }

        public void DrawImage(BitmapSource image, Rect rect, ImageFit fit)
        {
            if (image == null || rect.Width <= 0 || rect.Height <= 0) return;

            Rect dest = ImageFitMath.Fit(image.PixelWidth, image.PixelHeight, rect, fit);

            // Cover can overflow the slot — clip so it never bleeds onto neighbours.
            bool clip = fit == ImageFit.Cover;
            if (clip) PushClip(rect);
            _dc.DrawImage(image, dest);
            if (clip) Pop();
        }

        public void DrawText(string text, Rect rect, TextStyle style)
        {
            if (string.IsNullOrEmpty(text) || rect.Width <= 0 || rect.Height <= 0) return;

            var typeface = new Typeface(
                new FontFamily(string.IsNullOrWhiteSpace(style.FontFamily) ? "Tahoma" : style.FontFamily),
                style.Style,
                style.Weight,
                FontStretches.Normal);

            double size = Math.Max(style.FontSize, 1);

            // Shrink to fit before falling back to trimming.
            //
            // The field model has offered ShrinkToFit since the beginning — and it is
            // the DEFAULT — while this method only ever trimmed with an ellipsis. On a
            // run of several thousand records every unusually long name was quietly
            // cut short, and it is only visible once the job is printed.
            if (style.MinFontSize > 0 && style.MinFontSize < size)
            {
                var fit = CopyFitCalculator.Fit(
                    text, rect.Width, rect.Height, size, style.MinFontSize,
                    (t, s) => Measure(t, s, typeface, style));

                size = fit.FontSize;
            }

            var ft = Build(text, size, typeface, style, rect);
            _dc.DrawText(ft, rect.TopLeft);
        }

        /// <summary>Natural size of <paramref name="text"/> at a given font size.</summary>
        private static (double Width, double Height) Measure(
            string text, double fontSize, Typeface typeface, TextStyle style)
        {
            // Measured WRAPPED at the box width: the height that comes back is the
            // height the text will actually occupy, so a long line that wraps to three
            // lines is judged on those three lines rather than on one long one.
            var ft = new FormattedText(
                text, CultureInfo.CurrentUICulture, style.FlowDirection, typeface,
                Math.Max(fontSize, 0.1), Brushes.Black, pixelsPerDip: 1.0);

            return (ft.Width, ft.Height);
        }

        private static FormattedText Build(
            string text, double size, Typeface typeface, TextStyle style, Rect rect) =>
            new(text,
                CultureInfo.CurrentUICulture,
                style.FlowDirection,
                typeface,
                Math.Max(size, 1),
                style.Foreground ?? Brushes.Black,
                // Text is laid out in DIPs, so the pixels-per-DIP is 1 here; the
                // RenderTargetBitmap applies the export DPI afterwards.
                pixelsPerDip: 1.0)
            {
                MaxTextWidth = rect.Width,
                MaxTextHeight = rect.Height,
                TextAlignment = style.Alignment,
                // Still the last resort: a record that cannot fit even at the minimum
                // size is trimmed rather than allowed to spill over its neighbours.
                Trimming = TextTrimming.CharacterEllipsis,
            };

        public void PushClip(Rect rect)
        {
            var geo = new RectangleGeometry(rect);
            geo.Freeze();
            _dc.PushClip(geo);
            _pushCount++;
        }

        public void Pop()
        {
            if (_pushCount <= 0) return;
            _dc.Pop();
            _pushCount--;
        }
    }
}
