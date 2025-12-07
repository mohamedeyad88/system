using SkiaSharp;
using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Apex.NumberedBooksEngine.UI
{
    /// <summary>
    /// Extension methods for converting SkiaSharp images to WPF-compatible formats.
    /// </summary>
    public static class SKImageExtensions
    {
        /// <summary>
        /// Converts an SKImage to a WPF BitmapSource.
        /// </summary>
        public static BitmapSource ToBitmapSource(SKImage image)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));

            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = new MemoryStream();
            data.SaveTo(stream);
            stream.Position = 0;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze(); // Make it cross-thread accessible

            return bitmap;
        }

        /// <summary>
        /// Converts an SKBitmap to a WPF BitmapSource.
        /// </summary>
        public static BitmapSource ToBitmapSource(SKBitmap bitmap)
        {
            if (bitmap == null) throw new ArgumentNullException(nameof(bitmap));

            using var image = SKImage.FromBitmap(bitmap);
            return ToBitmapSource(image);
        }

        /// <summary>
        /// Converts SKImage to System.Drawing.Bitmap for printing.
        /// </summary>
        public static System.Drawing.Bitmap ToDrawingBitmap(SKImage image)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));

            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = new MemoryStream();
            data.SaveTo(stream);
            stream.Position = 0;

            return new System.Drawing.Bitmap(stream);
        }
    }
}
