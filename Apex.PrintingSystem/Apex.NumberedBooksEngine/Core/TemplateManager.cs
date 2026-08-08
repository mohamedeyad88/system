using Apex.NumberedBooksEngine.Models;
using SkiaSharp;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Apex.NumberedBooksEngine.Core
{
    /// <summary>
    /// Result of template upload to a printer.
    /// </summary>
    public record TemplateUploadResult(
        string TemplateId,
        bool StoredInPrinter,
        int Dpi,
        int WidthPx,
        int HeightPx,
        string Checksum);

    /// <summary>
    /// Options for template upload.
    /// </summary>
    public class TemplateUploadOptions
    {
        public int Dpi { get; set; } = 300;
        public bool ForceReupload { get; set; } = false;
    }

    /// <summary>
    /// Cached template entry.
    /// </summary>
    internal class CachedTemplate
    {
        public SKImage Image { get; set; } = null!;
        public string Checksum { get; set; } = "";
        public int Dpi { get; set; }
        public DateTime CachedAt { get; set; }
    }

    /// <summary>
    /// Manages template rasterization, caching, and printer uploads.
    /// Implements "template once" strategy by caching rasterized templates.
    /// </summary>
    public class TemplateManager : IDisposable
    {
        private readonly ConcurrentDictionary<string, CachedTemplate> _cache = new();
        private readonly ConcurrentDictionary<string, TemplateUploadResult> _printerTemplates = new();
        private bool _disposed;

        /// <summary>
        /// Rasterizes a template (PDF or image) at the specified DPI.
        /// Returns cached version if available with matching checksum and DPI.
        /// </summary>
        public (SKImage Image, string Checksum) RasterizeTemplate(string templatePath, int dpi = 300)
        {
            System.Diagnostics.Debug.WriteLine($"[NUMBERING] TemplateManager.RasterizeTemplate - Path: {templatePath}, DPI: {dpi}");

            // ═══════════════════════════════════════════════════════════════════
            // VALIDATION: Ensure template file exists and is accessible
            // ═══════════════════════════════════════════════════════════════════
            if (string.IsNullOrEmpty(templatePath))
            {
                throw new ArgumentException("مسار القالب فارغ. يرجى اختيار ملف قالب صالح.", nameof(templatePath));
            }

            if (!File.Exists(templatePath))
            {
                throw new FileNotFoundException($"ملف القالب غير موجود: {templatePath}", templatePath);
            }

            try
            {
                var checksum = ComputeFileChecksum(templatePath);
                var cacheKey = $"{checksum}_{dpi}";

                if (_cache.TryGetValue(cacheKey, out var cached))
                {
                    System.Diagnostics.Debug.WriteLine($"[NUMBERING] ✅ Template found in cache: {cacheKey}");
                    return (cached.Image, cached.Checksum);
                }

                System.Diagnostics.Debug.WriteLine($"[NUMBERING] Loading template from file...");

                // Open file with sharing to support OneDrive/cloud files
                using var stream = new FileStream(templatePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var result = RasterizeTemplate(stream, GetTemplateFormat(templatePath), dpi, checksum);

                System.Diagnostics.Debug.WriteLine($"[NUMBERING] ✅ Template loaded: {result.Image.Width}x{result.Image.Height}");
                return result;
            }
            catch (IOException ioEx)
            {
                System.Diagnostics.Debug.WriteLine($"[NUMBERING] ❌ IO Error loading template: {ioEx.Message}");
                throw new InvalidOperationException($"فشل في قراءة ملف القالب. تأكد من أن الملف غير مفتوح في برنامج آخر.\n{ioEx.Message}", ioEx);
            }
            catch (Exception ex) when (ex is not ArgumentException && ex is not FileNotFoundException && ex is not InvalidOperationException)
            {
                System.Diagnostics.Debug.WriteLine($"[NUMBERING] ❌ Error loading template: {ex.Message}");
                throw new InvalidOperationException($"حدث خطأ أثناء تحميل القالب: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Rasterizes a template from a stream.
        /// </summary>
        public (SKImage Image, string Checksum) RasterizeTemplate(Stream stream, TemplateFormat format, int dpi = 300, string? precomputedChecksum = null)
        {
            var checksum = precomputedChecksum ?? ComputeStreamChecksum(stream);
            var cacheKey = $"{checksum}_{dpi}";

            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                return (cached.Image, cached.Checksum);
            }

            SKImage image;
            if (format == TemplateFormat.Pdf)
            {
                image = RasterizePdf(stream, dpi);
            }
            else
            {
                image = RasterizeImage(stream);
            }

            var entry = new CachedTemplate
            {
                Image = image,
                Checksum = checksum,
                Dpi = dpi,
                CachedAt = DateTime.UtcNow
            };

            _cache[cacheKey] = entry;
            return (image, checksum);
        }

        /// <summary>
        /// Gets a cached template by checksum and DPI if available.
        /// </summary>
        public SKImage? GetCachedTemplate(string checksum, int dpi)
        {
            var cacheKey = $"{checksum}_{dpi}";
            return _cache.TryGetValue(cacheKey, out var cached) ? cached.Image : null;
        }

        /// <summary>
        /// Uploads a template to a printer for stored-form printing.
        /// Returns the template ID assigned by the printer or a generated ID.
        /// </summary>
        public async Task<TemplateUploadResult> UploadTemplateToPrinterAsync(
            string printerName,
            SKImage templateImage,
            string checksum,
            TemplateUploadOptions options,
            IPrintOutputService printService)
        {
            // Check if already uploaded to this printer
            var printerKey = $"{printerName}_{checksum}_{options.Dpi}";
            if (!options.ForceReupload && _printerTemplates.TryGetValue(printerKey, out var existing))
            {
                return existing;
            }

            // Generate a unique template ID
            var templateId = $"T-{Guid.NewGuid():N}".Substring(0, 12);

            // Attempt upload (implementation depends on printer capabilities)
            var storedInPrinter = false;

            // For now, we'll mark as "not stored in printer" and rely on spooler fallback
            // Real implementation would use PCL/PS commands via printService

            var result = new TemplateUploadResult(
                TemplateId: templateId,
                StoredInPrinter: storedInPrinter,
                Dpi: options.Dpi,
                WidthPx: templateImage.Width,
                HeightPx: templateImage.Height,
                Checksum: checksum
            );

            _printerTemplates[printerKey] = result;
            return await Task.FromResult(result);
        }

        /// <summary>
        /// Checks if a template is already uploaded to a printer.
        /// </summary>
        public TemplateUploadResult? GetPrinterTemplate(string printerName, string checksum, int dpi)
        {
            var printerKey = $"{printerName}_{checksum}_{dpi}";
            return _printerTemplates.TryGetValue(printerKey, out var result) ? result : null;
        }

        /// <summary>
        /// Clears the template cache for a specific printer.
        /// </summary>
        public void ClearPrinterTemplates(string printerName)
        {
            var keysToRemove = _printerTemplates.Keys
                .Where(k => k.StartsWith($"{printerName}_"))
                .ToList();

            foreach (var key in keysToRemove)
            {
                _printerTemplates.TryRemove(key, out _);
            }
        }

        private SKImage RasterizePdf(Stream stream, int dpi)
        {
            System.Diagnostics.Debug.WriteLine($"[NUMBERING] RasterizePdf - DPI: {dpi}");

            try
            {
                // Reset stream position
                if (stream.CanSeek)
                {
                    stream.Position = 0;
                }

                // Use PDFium to rasterize PDF at specified DPI
                using var doc = PdfiumViewer.PdfDocument.Load(stream);

                if (doc.PageCount == 0)
                {
                    throw new InvalidOperationException("ملف PDF فارغ أو تالف.");
                }

                System.Diagnostics.Debug.WriteLine($"[NUMBERING] PDF loaded: {doc.PageCount} pages");

                using var rendered = doc.Render(0, dpi, dpi, PdfiumViewer.PdfRenderFlags.Annotations);

                if (rendered == null)
                {
                    throw new InvalidOperationException("فشل في تحويل صفحة PDF إلى صورة.");
                }

                System.Diagnostics.Debug.WriteLine($"[NUMBERING] PDF rendered: {rendered.Width}x{rendered.Height}");

                using var bitmap = new System.Drawing.Bitmap(rendered);
                using var ms = new MemoryStream();
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;

                var image = SKImage.FromEncodedData(ms);

                if (image == null)
                {
                    throw new InvalidOperationException("فشل في إنشاء صورة من PDF المحول.");
                }

                System.Diagnostics.Debug.WriteLine($"[NUMBERING] ✅ PDF rasterized successfully: {image.Width}x{image.Height}");
                return image;
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                System.Diagnostics.Debug.WriteLine($"[NUMBERING] ❌ PDF rasterization failed: {ex.Message}");
                throw new InvalidOperationException($"فشل في معالجة ملف PDF. تأكد من صحة الملف.\n{ex.Message}", ex);
            }
        }

        private SKImage RasterizeImage(Stream stream)
        {
            System.Diagnostics.Debug.WriteLine($"[NUMBERING] RasterizeImage - Loading image from stream");

            try
            {
                // Reset stream position
                if (stream.CanSeek)
                {
                    stream.Position = 0;
                }

                var image = SKImage.FromEncodedData(stream);

                if (image == null)
                {
                    throw new InvalidOperationException("فشل في تحميل الصورة. تأكد من صحة تنسيق الملف (PNG, JPG, BMP).");
                }

                System.Diagnostics.Debug.WriteLine($"[NUMBERING] ✅ Image loaded: {image.Width}x{image.Height}");
                return image;
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                System.Diagnostics.Debug.WriteLine($"[NUMBERING] ❌ Image loading failed: {ex.Message}");
                throw new InvalidOperationException($"فشل في تحميل ملف الصورة.\n{ex.Message}", ex);
            }
        }

        private string ComputeFileChecksum(string path)
        {
            using var stream = File.OpenRead(path);
            return ComputeStreamChecksum(stream);
        }

        private string ComputeStreamChecksum(Stream stream)
        {
            var position = stream.Position;
            if (stream.CanSeek) stream.Position = 0;

            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(stream);

            if (stream.CanSeek) stream.Position = position;

            return Convert.ToHexString(hash).Substring(0, 16); // Truncate for readability
        }

        private TemplateFormat GetTemplateFormat(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".pdf" ? TemplateFormat.Pdf : TemplateFormat.Image;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var entry in _cache.Values)
            {
                entry.Image?.Dispose();
            }
            _cache.Clear();
        }
    }
}
