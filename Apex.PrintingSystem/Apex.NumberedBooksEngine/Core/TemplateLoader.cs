using Apex.NumberedBooksEngine.Models;
using SkiaSharp;
using System;
using System.IO;

namespace Apex.NumberedBooksEngine.Core
{
    public class TemplateLoader : IDisposable
    {
        private SKImage? _cachedImage;

        public SKImage LoadTemplate(Stream templateStream, TemplateFormat format)
        {
            if (_cachedImage != null) return _cachedImage;

            // Reset stream position if possible
            if (templateStream.CanSeek) templateStream.Position = 0;

            if (format == TemplateFormat.Pdf)
            {
                return LoadPdfTemplate(templateStream);
            }

            // Try loading as bitmap first (for images)
            var bitmap = SKBitmap.Decode(templateStream);
            if (bitmap == null)
            {
                throw new InvalidOperationException("Failed to decode template image (bitmap is null).");
            }

            _cachedImage = SKImage.FromBitmap(bitmap) ?? throw new InvalidOperationException("Failed to create SKImage from decoded template bitmap.");
            return _cachedImage;
        }

        private SKImage LoadPdfTemplate(Stream pdfStream)
        {
            try
            {
                using var doc = PdfiumViewer.PdfDocument.Load(pdfStream);
                // Rasterize at high DPI (e.g., 300 DPI) for print quality
                int dpi = 300;
                using var image = doc.Render(0, dpi, dpi, PdfiumViewer.PdfRenderFlags.Annotations);

                // Convert System.Drawing.Image to SkiaSharp SKImage
                using var ms = new MemoryStream();
                image.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;

                var skBitmap = SKBitmap.Decode(ms);
                if (skBitmap == null)
                    throw new InvalidOperationException("Failed to decode PDF render to bitmap (skBitmap is null).");

                _cachedImage = SKImage.FromBitmap(skBitmap) ?? throw new InvalidOperationException("Failed to create SKImage from PDF bitmap.");
                return _cachedImage;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to rasterize PDF template: {ex.Message}", ex);
            }
        }

        public void Dispose()
        {
            _cachedImage?.Dispose();
        }
    }
}
