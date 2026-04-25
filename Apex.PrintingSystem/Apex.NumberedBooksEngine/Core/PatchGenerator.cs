using SkiaSharp;
using Apex.NumberedBooksEngine.Models;
using System;

namespace Apex.NumberedBooksEngine.Core
{
    public class PatchGenerator
    {
        // Render scale: 3× gives ~288 DPI on a 96 DPI base template,
        // which eliminates pixelation on screen previews and printed output.
        private const float RenderScale = 3f;

        public SKImage GeneratePatch(string text, SlotSpec slot, CopyStyle? copyStyle)
        {
            // Determine color and opacity
            var colorHex = copyStyle?.ColorHex ?? "#000000";
            var opacity  = copyStyle?.Opacity ?? 1.0f;

            if (!SKColor.TryParse(colorHex, out var color))
                color = SKColors.Black;
            color = color.WithAlpha((byte)(opacity * 255));

            // Scale font size up for high-DPI rendering
            float scaledFontSize = slot.FontSize * RenderScale;

            // Setup Paint – render at RenderScale resolution
            using var paint = new SKPaint
            {
                Color          = color,
                TextSize       = scaledFontSize,
                IsAntialias    = true,
                SubpixelText   = true,
                FilterQuality  = SKFilterQuality.High,
                Typeface       = SKTypeface.FromFamilyName(
                                     slot.FontFamily,
                                     SKFontStyle.Normal)
            };

            // Measure at scaled size
            var textBounds = new SKRect();
            paint.MeasureText(text, ref textBounds);

            // Surface at high-DPI size (padded 8 px per side at scale)
            int pad    = (int)Math.Ceiling(8 * RenderScale);
            int width  = (int)Math.Ceiling(textBounds.Width)  + pad * 2;
            int height = (int)Math.Ceiling(textBounds.Height) + pad * 2;
            if (width  < 1) width  = 1;
            if (height < 1) height = 1;

            var imageInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(imageInfo);
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);

            // Draw text at scaled coordinates
            canvas.DrawText(text, -textBounds.Left + pad, -textBounds.Top + pad, paint);

            // Snapshot at high resolution, then scale back down so the
            // Composer still works with logical (1×) coordinates.
            using var hiResImage = surface.Snapshot();

            int logicalW = (int)Math.Ceiling(hiResImage.Width  / RenderScale);
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
    }
}
