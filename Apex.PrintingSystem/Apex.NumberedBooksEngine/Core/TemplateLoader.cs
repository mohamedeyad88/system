using Apex.NumberedBooksEngine.Models;
using SkiaSharp;
using System;
using System.IO;

namespace Apex.NumberedBooksEngine.Core
{
    public class TemplateLoader : IDisposable
    {
        /// <summary>
        /// Decodes a template into a fresh <see cref="SKImage"/>. The caller owns the
        /// result and must dispose it.
        ///
        /// This used to cache the decoded image in a field and hand the SAME instance to
        /// every caller — while all three call sites wrap it in `using`. So the first
        /// caller freed the shared image and the cache went on serving that dangling
        /// handle: the next call touched freed native memory and SkiaSharp took the whole
        /// process down with an AccessViolationException, which .NET cannot catch. In
        /// practice that meant generating a live preview and then a multi-page preview
        /// (or printing after any preview) killed the app with the operator's job open.
        /// The cache also never keyed on WHICH template was asked for, so loading a
        /// second design would have silently re-rendered the first. It saved nothing —
        /// every call site loads once per job — so it is gone.
        /// </summary>
        public SKImage LoadTemplate(Stream templateStream, TemplateFormat format)
        {
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

            return SKImage.FromBitmap(bitmap) ?? throw new InvalidOperationException("Failed to create SKImage from decoded template bitmap.");
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

                return SKImage.FromBitmap(skBitmap) ?? throw new InvalidOperationException("Failed to create SKImage from PDF bitmap.");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to rasterize PDF template: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Nothing to release: every decoded image belongs to whoever asked for it.
        /// Kept so existing `using` blocks around the loader keep compiling.
        /// </summary>
        public void Dispose()
        {
        }
    }
}
