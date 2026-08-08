using System.Windows;
using System.Windows.Media;
using Apex.UI.ViewModels;

namespace Apex.UI.Rendering
{
    /// <summary>
    /// THE single template painting algorithm.
    ///
    /// Both the live preview and every exported file go through this method, so the
    /// two can no longer disagree. Before it existed the preview was a XAML
    /// DataTemplate (images fitted <c>Uniform</c>, one error style) while export was
    /// hand-written <c>DrawingContext</c> calls (images stretched, a different error
    /// style) — the classic "what you see is not what you print" bug.
    ///
    /// All geometry is in WPF DIPs, taken from the *Dip properties of the model.
    /// </summary>
    public static class TemplatePainter
    {
        // ── Error presentation (single definition, used everywhere) ────────────
        private static readonly Brush ErrorFill = Frozen(new SolidColorBrush(Color.FromArgb(0xBF, 0x1A, 0x00, 0x00)));
        private static readonly Brush ErrorStroke = Frozen(new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)));
        private const double ErrorStrokeThickness = 1.5;
        private const double ErrorFontSize = 8;

        /// <summary>Placeholder shown when a QR/barcode value could not be encoded.</summary>
        private static readonly Brush CodeFallbackFill = Frozen(new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E)));
        private static readonly Brush CodeFallbackStroke = Frozen(new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)));
        private static readonly Brush CodeFallbackText = Frozen(new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)));
        private const double CodeFallbackFontSize = 7;

        private static Brush Frozen(Brush b) { b.Freeze(); return b; }

        /// <summary>Paints <paramref name="template"/> onto <paramref name="target"/>.</summary>
        public static void Paint(RenderedTemplate template, IRenderTarget target)
        {
            if (template == null || target == null) return;

            var page = new Rect(0, 0, template.WidthDip, template.HeightDip);

            // ── Page background ────────────────────────────────────────────────
            target.FillRect(page, template.BackgroundBrush);

            if (template.BackgroundImage != null)
                target.DrawImage(template.BackgroundImage, page, ImageFit.Stretch);

            // ── Fields ─────────────────────────────────────────────────────────
            foreach (var f in template.Fields)
            {
                var rect = new Rect(f.LeftDip, f.TopDip, f.WidthDip, f.HeightDip);

                // Nothing may spill outside its own slot.
                target.PushClip(rect);
                try
                {
                    PaintField(f, rect, target);
                }
                finally
                {
                    target.Pop();
                }
            }
        }

        private static void PaintField(RenderedFieldItem f, Rect rect, IRenderTarget target)
        {
            if (f.IsError)
            {
                target.FillRect(rect, ErrorFill);
                target.StrokeRect(rect, ErrorStroke, ErrorStrokeThickness);
                target.DrawText(f.ErrorMessage, Deflate(rect, 2), TextStyleFor(f, ErrorFontSize, ErrorStroke));
                return;
            }

            // Any field carrying a bitmap paints as an image — Image slots AND the
            // encoded QR/barcode symbols.
            if (f.HasImage)
            {
                target.DrawImage(f.ImageSource!, rect, ParseFit(f.ImageFitMode));
                return;
            }

            // QR/barcode that could not be encoded: show the raw value on a plate so
            // the operator can spot and fix the data.
            if (f.IsQrField || f.IsBarcodeField)
            {
                target.FillRect(rect, CodeFallbackFill);
                target.StrokeRect(rect, CodeFallbackStroke, 1);
                target.DrawText(f.RenderedText, Deflate(rect, 2),
                    TextStyleFor(f, CodeFallbackFontSize, CodeFallbackText, TextAlignment.Center));
                return;
            }

            if (f.IsTextField)
                target.DrawText(f.RenderedText, rect, TextStyleFor(f, f.FontSize, f.TextBrush));
        }

        /// <summary>Maps the slot's fit mode string onto the painter's enum.</summary>
        internal static ImageFit ParseFit(string? mode) => (mode ?? "").Trim().ToLowerInvariant() switch
        {
            "cover" => ImageFit.Cover,
            "stretch" or "fill" => ImageFit.Stretch,
            _ => ImageFit.Contain,
        };

        private static TextStyle TextStyleFor(
            RenderedFieldItem f, double fontSize, Brush foreground, TextAlignment? align = null) =>
            new()
            {
                FontFamily = f.FontFamily,
                FontSize = fontSize,
                Weight = f.WpfFontWeight,
                Style = f.WpfFontStyle,
                Foreground = foreground,
                Alignment = align ?? f.WpfTextAlignment,
                FlowDirection = f.WpfFlowDirection,
            };

        private static Rect Deflate(Rect r, double by)
        {
            double w = System.Math.Max(0, r.Width - by * 2);
            double h = System.Math.Max(0, r.Height - by * 2);
            return new Rect(r.X + by, r.Y + by, w, h);
        }
    }
}
