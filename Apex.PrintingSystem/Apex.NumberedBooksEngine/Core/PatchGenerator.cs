using SkiaSharp;
using Apex.NumberedBooksEngine.Models;
using System;

namespace Apex.NumberedBooksEngine.Core
{
    public class PatchGenerator
    {
        public SKImage GeneratePatch(string text, SlotSpec slot, CopyStyle? copyStyle)
        {
            // Determine color and opacity
            var colorHex = copyStyle?.ColorHex ?? "#000000";
            var opacity = copyStyle?.Opacity ?? 1.0f;
            
            if (!SKColor.TryParse(colorHex, out var color))
            {
                color = SKColors.Black;
            }
            color = color.WithAlpha((byte)(opacity * 255));

            // Setup Paint
            using var paint = new SKPaint
            {
                Color = color,
                TextSize = slot.FontSize,
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName(slot.FontFamily)
            };

            // Measure Text
            var textBounds = new SKRect();
            paint.MeasureText(text, ref textBounds);
            
            // Create Surface
            // Add some padding
            int width = (int)Math.Ceiling(textBounds.Width + 10);
            int height = (int)Math.Ceiling(textBounds.Height + 10);
            
            // Ensure valid dimensions
            if (width <= 0) width = 1;
            if (height <= 0) height = 1;

            using var surface = SKSurface.Create(new SKImageInfo(width, height));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);

            // Draw Text
            // Adjust coordinates to draw within bounds
            canvas.DrawText(text, -textBounds.Left + 5, -textBounds.Top + 5, paint);

            return surface.Snapshot();
        }
    }
}
