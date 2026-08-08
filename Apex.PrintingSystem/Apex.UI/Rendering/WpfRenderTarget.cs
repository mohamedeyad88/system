using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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

            var ft = new FormattedText(
                text,
                CultureInfo.CurrentUICulture,
                style.FlowDirection,
                new Typeface(
                    new FontFamily(string.IsNullOrWhiteSpace(style.FontFamily) ? "Tahoma" : style.FontFamily),
                    style.Style,
                    style.Weight,
                    FontStretches.Normal),
                Math.Max(style.FontSize, 1),
                style.Foreground ?? Brushes.Black,
                // Text is laid out in DIPs, so the pixels-per-DIP is 1 here; the
                // RenderTargetBitmap applies the export DPI afterwards.
                pixelsPerDip: 1.0)
            {
                MaxTextWidth = rect.Width,
                MaxTextHeight = rect.Height,
                TextAlignment = style.Alignment,
                Trimming = TextTrimming.CharacterEllipsis,
            };

            _dc.DrawText(ft, rect.TopLeft);
        }

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
