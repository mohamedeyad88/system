using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfSharpCore.Drawing;

namespace Apex.UI.Rendering
{
    /// <summary>
    /// Draws a template into a PDF page as TRUE VECTOR.
    ///
    /// Why glyph outlines instead of PDF text runs:
    /// the product is Arabic-first, and Arabic needs contextual shaping plus bidi
    /// reordering. WPF already does both correctly — it is what draws the on-screen
    /// preview. Re-shaping the text with a second engine risks disconnected letters
    /// and reversed runs, a regression far worse than the raster output this
    /// replaces. So the text is shaped ONCE by WPF and its outlines are emitted as
    /// vector paths: identical to the preview, resolution-independent on press.
    ///
    /// Trade-off: the text is not selectable/searchable in the PDF. That is the
    /// normal, accepted trade for print production (outlining text is routine), and
    /// it is the reason this target is used for OUTPUT only, never for preview.
    ///
    /// Coordinates arrive in WPF DIPs (1/96"); the constructor installs a scale so
    /// they land correctly in PDF points (1/72").
    /// </summary>
    public sealed class PdfRenderTarget : IRenderTarget, IDisposable
    {
        private readonly XGraphics _gfx;
        private readonly Stack<XGraphicsState> _states = new();

        /// <summary>Flattening tolerance in DIPs — well below a printer dot at 300 dpi.</summary>
        private const double FlattenTolerance = 0.02;

        public PdfRenderTarget(XGraphics gfx)
        {
            _gfx = gfx ?? throw new ArgumentNullException(nameof(gfx));
            // DIP (1/96") → point (1/72").
            _gfx.ScaleTransform(72.0 / 96.0);
        }

        // ── Shapes ────────────────────────────────────────────────────────────

        public void FillRect(Rect rect, Brush brush)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return;
            var color = ColorOf(brush);
            if (color == null) return;
            _gfx.DrawRectangle(new XSolidBrush(color.Value), ToXRect(rect));
        }

        public void StrokeRect(Rect rect, Brush stroke, double thickness)
        {
            if (thickness <= 0 || rect.Width <= 0 || rect.Height <= 0) return;
            var color = ColorOf(stroke);
            if (color == null) return;
            _gfx.DrawRectangle(new XPen(color.Value, thickness), ToXRect(rect));
        }

        // ── Images ────────────────────────────────────────────────────────────

        public void DrawImage(BitmapSource image, Rect rect, ImageFit fit)
        {
            if (image == null || rect.Width <= 0 || rect.Height <= 0) return;

            byte[]? png = EncodePng(image);
            if (png == null) return;

            using var ms = new MemoryStream(png);
            using var ximg = XImage.FromStream(() => ms);

            Rect dest = ImageFitMath.Fit(image.PixelWidth, image.PixelHeight, rect, fit);

            bool clip = fit == ImageFit.Cover;
            if (clip) PushClip(rect);
            _gfx.DrawImage(ximg, ToXRect(dest));
            if (clip) Pop();
        }

        private static byte[]? EncodePng(BitmapSource src)
        {
            try
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(src));
                using var ms = new MemoryStream();
                enc.Save(ms);
                return ms.ToArray();
            }
            catch { return null; }
        }

        // ── Text (vector outlines) ────────────────────────────────────────────

        public void DrawText(string text, Rect rect, TextStyle style)
        {
            if (string.IsNullOrEmpty(text) || rect.Width <= 0 || rect.Height <= 0) return;

            var color = ColorOf(style.Foreground) ?? XColors.Black;

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
                Brushes.Black,
                pixelsPerDip: 1.0)
            {
                MaxTextWidth = rect.Width,
                MaxTextHeight = rect.Height,
                TextAlignment = style.Alignment,
                Trimming = TextTrimming.CharacterEllipsis,
            };

            // WPF shapes + lays out the text here; we only harvest the outlines.
            Geometry geo = ft.BuildGeometry(rect.TopLeft);
            var path = ToXPath(geo);
            if (path == null) return;

            _gfx.DrawPath(new XSolidBrush(color), path);
        }

        /// <summary>Converts a WPF geometry into a PdfSharp path by flattening to polygons.</summary>
        private static XGraphicsPath? ToXPath(Geometry geo)
        {
            PathGeometry flat;
            try { flat = geo.GetFlattenedPathGeometry(FlattenTolerance, ToleranceType.Absolute); }
            catch { return null; }

            if (flat.Figures.Count == 0) return null;

            var path = new XGraphicsPath
            {
                // Glyph outlines rely on the winding rule to cut counters (the hole
                // in a "ه" or "و"); using the wrong rule fills them solid.
                FillMode = flat.FillRule == FillRule.EvenOdd ? XFillMode.Alternate : XFillMode.Winding,
            };

            bool anyAdded = false;
            foreach (var figure in flat.Figures)
            {
                var pts = new List<XPoint> { new(figure.StartPoint.X, figure.StartPoint.Y) };

                foreach (var seg in figure.Segments)
                {
                    switch (seg)
                    {
                        case PolyLineSegment poly:
                            foreach (var p in poly.Points) pts.Add(new XPoint(p.X, p.Y));
                            break;
                        case LineSegment line:
                            pts.Add(new XPoint(line.Point.X, line.Point.Y));
                            break;
                        // Flattening leaves only line segments; anything else is ignored.
                    }
                }

                if (pts.Count < 3) continue;   // degenerate contour
                path.AddPolygon(pts.ToArray());
                anyAdded = true;
            }

            return anyAdded ? path : null;
        }

        // ── Clipping ──────────────────────────────────────────────────────────

        public void PushClip(Rect rect)
        {
            _states.Push(_gfx.Save());
            _gfx.IntersectClip(ToXRect(rect));
        }

        public void Pop()
        {
            if (_states.Count == 0) return;
            _gfx.Restore(_states.Pop());
        }

        public void Dispose()
        {
            while (_states.Count > 0) Pop();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static XRect ToXRect(Rect r) => new(r.X, r.Y, r.Width, r.Height);

        /// <summary>Solid colour of a brush, or null when it cannot be represented.</summary>
        private static XColor? ColorOf(Brush? brush)
        {
            if (brush is not SolidColorBrush s) return null;
            var c = s.Color;
            return XColor.FromArgb(c.A, c.R, c.G, c.B);
        }
    }
}
