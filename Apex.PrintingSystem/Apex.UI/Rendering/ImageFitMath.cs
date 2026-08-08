using System;
using System.Windows;

namespace Apex.UI.Rendering
{
    /// <summary>
    /// Where an image lands inside its slot for each <see cref="ImageFit"/> mode.
    ///
    /// Shared by every render target so the screen preview and the exported file
    /// cannot disagree about image placement — that divergence is exactly the bug
    /// the unified painter was introduced to remove.
    /// </summary>
    internal static class ImageFitMath
    {
        public static Rect Fit(double imageWidth, double imageHeight, Rect bounds, ImageFit fit)
        {
            if (fit == ImageFit.Stretch) return bounds;
            if (imageWidth <= 0 || imageHeight <= 0) return bounds;

            double sx = bounds.Width / imageWidth;
            double sy = bounds.Height / imageHeight;
            double scale = fit == ImageFit.Cover ? Math.Max(sx, sy) : Math.Min(sx, sy);

            double w = imageWidth * scale;
            double h = imageHeight * scale;

            // Centre within the slot.
            return new Rect(
                bounds.X + (bounds.Width - w) / 2.0,
                bounds.Y + (bounds.Height - h) / 2.0,
                w, h);
        }
    }
}
