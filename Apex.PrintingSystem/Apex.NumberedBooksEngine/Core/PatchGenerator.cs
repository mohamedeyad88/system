using SkiaSharp;
using SkiaSharp.HarfBuzz;
using Apex.NumberedBooksEngine.Models;
using System;

namespace Apex.NumberedBooksEngine.Core
{
    public class PatchGenerator
    {
        // Render scale: extra over-render factor for antialiasing sharpness.
        private const float RenderScale = 3f;

        // Design DPI: WPF design-time coordinate space uses 96 DPI.
        private const float DesignDpi = 96f;

        /// <summary>
        /// Generates a text patch image for the given slot.
        /// </summary>
        /// <param name="text">Text to render.</param>
        /// <param name="slot">Slot specification (font size is in design units at 96 DPI).</param>
        /// <param name="copyStyle">Optional copy style override.</param>
        /// <param name="dpiScale">
        ///   Ratio of template DPI to design DPI (templateDpi / 96).
        ///   Pass 1.0 for screen/preview, ~3.125 for 300 DPI print templates.
        ///   This ensures numbers appear at the correct physical size on the canvas.
        /// </param>
        public SKImage GeneratePatch(string text, SlotSpec slot, CopyStyle? copyStyle, float dpiScale = 1f)
        {
            // Determine color and opacity
            var colorHex = copyStyle?.ColorHex ?? "#000000";
            var opacity  = copyStyle?.Opacity ?? 1.0f;

            if (!SKColor.TryParse(colorHex, out var color))
                color = SKColors.Black;
            color = color.WithAlpha((byte)(opacity * 255));

            // Scale font size: design→print DPI, then ×RenderScale for antialiasing
            float scaledFontSize = slot.FontSize * dpiScale * RenderScale;

            // Setup Paint – render at RenderScale resolution
            using var paint = new SKPaint
            {
                Color = color,
                TextSize = scaledFontSize,
                IsAntialias = true,
                SubpixelText = true,
                FilterQuality = SKFilterQuality.High,
                Typeface = SKTypeface.FromFamilyName(
                                     slot.FontFamily,
                                     SKFontStyle.Normal)
            };

            // Measure at scaled size. MeasureText uses each glyph's full advance, so the
            // unshaped width is a safe upper bound for the shaped (Arabic-joined) width.
            var textBounds = new SKRect();
            paint.MeasureText(text, ref textBounds);
            float advanceWidth = paint.MeasureText(text);

            // Surface at high-DPI size (padded 8 px per side at scale)
            int pad = (int)Math.Ceiling(8 * RenderScale);
            int width = (int)Math.Ceiling(Math.Max(textBounds.Width, advanceWidth)) + pad * 2;
            int height = (int)Math.Ceiling(textBounds.Height) + pad * 2;
            if (width < 1) width = 1;
            if (height < 1) height = 1;

            var imageInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(imageInfo);
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);

            // Draw text at scaled coordinates. Split into runs so Arabic-LETTER runs are
            // HarfBuzz-shaped (correct joining for labels like "أصل"/"صورة") while DIGIT
            // runs (Latin or Arabic-Indic) stay plain LTR — never bidi-reversed. This is
            // critical: shaping the whole "1001 / صورة" string reverses the number
            // (1002 → 2001). Runs are laid out in logical left-to-right order.
            float baselineX = -textBounds.Left + pad;
            float baselineY = -textBounds.Top + pad;
            float penX = baselineX;
            foreach ((string runText, bool isArabicLetters) in SplitDirectionalRuns(text))
            {
                if (isArabicLetters)
                {
                    try
                    {
                        using var shaper = new SKShaper(paint.Typeface);
                        canvas.DrawShapedText(shaper, runText, penX, baselineY, paint);
                    }
                    catch
                    {
                        canvas.DrawText(runText, penX, baselineY, paint);
                    }
                }
                else
                {
                    canvas.DrawText(runText, penX, baselineY, paint);
                }
                penX += paint.MeasureText(runText);
            }

            // Snapshot the hi-res surface, then scale back to print-DPI size.
            // Final size = slot.FontSize × dpiScale (correct physical size on template canvas).
            using var hiResImage = surface.Snapshot();

            int logicalW = (int)Math.Ceiling(hiResImage.Width / RenderScale);
            int logicalH = (int)Math.Ceiling(hiResImage.Height / RenderScale);
            if (logicalW < 1) logicalW = 1;
            if (logicalH < 1) logicalH = 1;

            var downInfo = new SKImageInfo(logicalW, logicalH, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var downSurface = SKSurface.Create(downInfo);
            var downCanvas = downSurface.Canvas;
            downCanvas.Clear(SKColors.Transparent);

            using var downPaint = new SKPaint { FilterQuality = SKFilterQuality.High };
            downCanvas.DrawImage(hiResImage,
                                 new SKRect(0, 0, hiResImage.Width, hiResImage.Height),
                                 new SKRect(0, 0, logicalW, logicalH),
                                 downPaint);

            return downSurface.Snapshot();
        }

        /// <summary>
        /// True for Arabic LETTERS (the glyphs that need contextual joining) — deliberately
        /// EXCLUDES Arabic-Indic digits (U+0660–U+0669), so numbers are never shaped/reversed.
        /// </summary>
        private static bool IsArabicLetter(char c) =>
            (c >= 'ؠ' && c <= 'ٟ') ||   // Arabic letters + marks (excludes digits U+0660–U+0669)
            (c >= 'ٰ' && c <= 'ۓ') ||   // more Arabic letters
            (c >= 'ﭐ' && c <= '﷿') ||   // Arabic Presentation Forms-A
            (c >= 'ﹰ' && c <= '﻿');     // Arabic Presentation Forms-B

        /// <summary>Splits text into consecutive runs of Arabic letters vs. everything else.</summary>
        private static System.Collections.Generic.List<(string Text, bool IsArabicLetters)> SplitDirectionalRuns(string text)
        {
            var runs = new System.Collections.Generic.List<(string, bool)>();
            if (string.IsNullOrEmpty(text)) return runs;

            int start = 0;
            bool current = IsArabicLetter(text[0]);
            for (int i = 1; i < text.Length; i++)
            {
                bool isArabic = IsArabicLetter(text[i]);
                if (isArabic != current)
                {
                    runs.Add((text.Substring(start, i - start), current));
                    start = i;
                    current = isArabic;
                }
            }
            runs.Add((text.Substring(start), current));
            return runs;
        }
    }
}
