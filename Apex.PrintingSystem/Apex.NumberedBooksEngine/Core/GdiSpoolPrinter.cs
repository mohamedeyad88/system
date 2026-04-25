using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.NumberedBooksEngine.Core
{
    /// <summary>
    /// GDI-based printer wrapper for cycle-based printing.
    /// Uses WindowsPrintSpoolerService internally with template caching support.
    /// </summary>
    public class GdiSpoolPrinter : IDisposable
    {
        private readonly WindowsPrintSpoolerService _spoolerService;
        private SKImage? _cachedTemplate;
        private SKBitmap? _cachedTemplateBitmap; // Guaranteed raster copy for reliable pixel access
        private int _currentCopyIndex = 0;
        private bool _templateReady = false;

        public GdiSpoolPrinter()
        {
            _spoolerService = new WindowsPrintSpoolerService();
        }

        /// <summary>
        /// CRITICAL: Reset all state before starting a new job.
        /// Must be called before SetCachedTemplate for each new job.
        /// </summary>
        public void ResetForNewJob()
        {
            _cachedTemplate?.Dispose();
            _cachedTemplate = null;
            _cachedTemplateBitmap?.Dispose();
            _cachedTemplateBitmap = null;
            _currentCopyIndex = 0;
            _templateReady = false;
            
            System.Diagnostics.Debug.WriteLine($"[NUMBERING] GdiSpoolPrinter reset for new job");
        }

        /// <summary>
        /// Sets the cached template image (rasterized once, reused for all pages).
        /// CRITICAL: Uses guaranteed raster copy to prevent pixmap null errors.
        /// </summary>
        public void SetCachedTemplate(SKImage templateImage)
        {
            if (templateImage == null)
                throw new ArgumentNullException(nameof(templateImage), "Template image cannot be null");
            
            // Reset any previous template
            _cachedTemplate?.Dispose();
            _cachedTemplateBitmap?.Dispose();
            _cachedTemplate = null;
            _cachedTemplateBitmap = null;
            _templateReady = false;
            
            System.Diagnostics.Debug.WriteLine($"[NUMBERING] SetCachedTemplate - Input size: {templateImage.Width}x{templateImage.Height}");
            
            // ═══════════════════════════════════════════════════════════════════
            // CRITICAL FIX: Always create a guaranteed raster bitmap copy
            // Do NOT use PeekPixels - it can return null for non-raster images
            // Instead, encode to PNG and decode to ensure we have a raster copy
            // ═══════════════════════════════════════════════════════════════════
            
            try
            {
                // Encode to PNG (lossless, preserves quality)
                using var encoded = templateImage.Encode(SKEncodedImageFormat.Png, 100);
                if (encoded == null)
                {
                    throw new InvalidOperationException("Failed to cache template: could not encode image to PNG.");
                }
                
                // Decode to create guaranteed raster bitmap
                _cachedTemplateBitmap = SKBitmap.Decode(encoded);
                if (_cachedTemplateBitmap == null)
                {
                    throw new InvalidOperationException("Failed to cache template: could not decode PNG to bitmap.");
                }
                
                // Create SKImage from bitmap for composing
                _cachedTemplate = SKImage.FromBitmap(_cachedTemplateBitmap);
                if (_cachedTemplate == null)
                {
                    _cachedTemplateBitmap.Dispose();
                    _cachedTemplateBitmap = null;
                    throw new InvalidOperationException("Failed to cache template: could not create SKImage from bitmap.");
                }
                
                _templateReady = true;
                System.Diagnostics.Debug.WriteLine($"[NUMBERING] ✅ Template cached successfully - Size: {_cachedTemplate.Width}x{_cachedTemplate.Height}");
            }
            catch (Exception ex)
            {
                _templateReady = false;
                System.Diagnostics.Debug.WriteLine($"[NUMBERING] ❌ SetCachedTemplate failed: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// Checks if template is ready for printing.
        /// </summary>
        public bool IsTemplateReady => _templateReady && _cachedTemplate != null;

        /// <summary>
        /// Sets the current copy index for tray routing (0 = Original, 1 = Copy 1, etc.)
        /// </summary>
        public void SetCurrentCopyIndex(int copyIndex)
        {
            _currentCopyIndex = copyIndex;
            _spoolerService.SetCurrentCopyIndex(copyIndex);
        }

        /// <summary>
        /// Starts a print job with the given settings.
        /// </summary>
        public Task StartJobAsync(PrintJobSettings settings, CancellationToken ct)
        {
            return _spoolerService.StartJobAsync(settings, ct);
        }

        /// <summary>
        /// Prints a page with overlays (numbers) on top of the cached template.
        /// </summary>
        public async Task PrintPageWithOverlaysAsync(PagePrintCommand command)
        {
            if (_cachedTemplate == null)
                throw new InvalidOperationException("Template not set. Call SetCachedTemplate first.");

            // Convert PagePrintCommand to GdiPagePrintCommand
            var gdiCommand = new GdiPagePrintCommand
            {
                PageNumbers = ExtractPageNumbers(command),
                Slots = ExtractSlots(command),
                CopyType = Models.CopyType.Original // Default, can be enhanced later
            };

            await PrintPageWithOverlaysAsync(gdiCommand);
        }

        /// <summary>
        /// Prints a page with overlays (numbers) on top of the cached template.
        /// CRITICAL: Validates template is ready and page generation succeeds before printing.
        /// </summary>
        public async Task PrintPageWithOverlaysAsync(GdiPagePrintCommand command)
        {
            // ═══════════════════════════════════════════════════════════════════
            // FAIL-FAST: Validate template is ready before attempting to compose
            // ═══════════════════════════════════════════════════════════════════
            if (!_templateReady || _cachedTemplate == null)
            {
                throw new InvalidOperationException(
                    "فشل في تحضير القالب للطباعة. يرجى التأكد من صحة ملف القالب وإعادة المحاولة.");
            }

            // Compose the page with overlays
            var composer = new Composer();
            
            // Create page assignment from command - FRESH for each page
            var slotAssignments = new List<SlotAssignment>();
            for (int i = 0; i < command.PageNumbers.Length && i < command.Slots.Count; i++)
            {
                slotAssignments.Add(new SlotAssignment(command.Slots[i].Id, command.PageNumbers[i]));
            }
            
            var pageAssignment = new PageAssignment(0, slotAssignments);
            
            System.Diagnostics.Debug.WriteLine($"[NUMBERING] Composing page - Numbers: [{string.Join(", ", command.PageNumbers)}], CopyType: {command.CopyType}");
            
            var pageImage = composer.ComposePageFromAssignment(
                _cachedTemplate,
                pageAssignment,
                command.Slots,
                command.CopyType);
            
            // ═══════════════════════════════════════════════════════════════════
            // FAIL-FAST: Validate page image was created successfully
            // ═══════════════════════════════════════════════════════════════════
            if (pageImage == null)
            {
                throw new InvalidOperationException(
                    "فشل في إنشاء صورة الصفحة للطباعة. تأكد من صحة إعدادات الترقيم.");
            }
            
            System.Diagnostics.Debug.WriteLine($"[NUMBERING] ✅ Page composed - Size: {pageImage.Width}x{pageImage.Height}");

            // ═══════════════════════════════════════════════════════════════════
            // CRITICAL: Do NOT dispose pageImage here!
            // The spooler will queue it and PrintDocument_PrintPage will dispose
            // it after actual printing completes. Disposing here causes
            // AccessViolationException when PrintDocument tries to use the image.
            // ═══════════════════════════════════════════════════════════════════
            await _spoolerService.PrintPageAsync(pageImage);
            
            System.Diagnostics.Debug.WriteLine($"[NUMBERING] ✅ Page sent to spooler queue");
        }

        /// <summary>
        /// Ends the current print job.
        /// </summary>
        public Task EndJobAsync()
        {
            return _spoolerService.EndJobAsync();
        }

        private long[] ExtractPageNumbers(PagePrintCommand command)
        {
            // Extract page numbers from slot overlays
            // This is a simplified extraction - may need enhancement based on actual usage
            return command.Slots.Select(s => long.TryParse(s.Text, out var num) ? num : -1)
                .Where(n => n >= 0)
                .ToArray();
        }

        private IReadOnlyList<Models.SlotSpec> ExtractSlots(PagePrintCommand command)
        {
            // Convert SlotOverlayCommand to SlotSpec
            return command.Slots.Select(s => new Models.SlotSpec(
                Id: s.SlotId,
                X: s.NormalizedX,
                Y: s.NormalizedY,
                Width: 0.1f, // Default width
                Height: 0.05f, // Default height
                FontFamily: s.FontFamily,
                FontSize: s.FontSize,
                FontColorHex: s.ColorHex,
                Align: Models.TextAlign.Left,
                Rotation: 0,
                CopyStyles: null
            )).ToList();
        }

        public void Dispose()
        {
            _cachedTemplate?.Dispose();
            _cachedTemplate = null;
            _cachedTemplateBitmap?.Dispose();
            _cachedTemplateBitmap = null;
            _templateReady = false;
            _spoolerService.Cancel();
            
            System.Diagnostics.Debug.WriteLine($"[NUMBERING] GdiSpoolPrinter disposed");
        }
    }

    /// <summary>
    /// Command for printing a page with overlays (GDI-specific).
    /// </summary>
    public class GdiPagePrintCommand
    {
        public long[] PageNumbers { get; set; } = Array.Empty<long>();
        public IReadOnlyList<Models.SlotSpec> Slots { get; set; } = Array.Empty<Models.SlotSpec>();
        public Models.CopyType CopyType { get; set; } = Models.CopyType.Original;
    }
}

