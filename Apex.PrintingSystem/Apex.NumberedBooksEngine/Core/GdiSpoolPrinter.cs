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

        // One composer for the whole job. It used to be constructed per page — a fresh
        // TemplateLoader and PatchGenerator for every sheet — and, worse, with default
        // number formatting, so the prefix, suffix and digit count the operator chose were
        // silently dropped at print time. ConfigureNumberFormat sets it once per job.
        private readonly Composer _composer = new();
        private SKImage? _cachedTemplate;
        private SKBitmap? _cachedTemplateBitmap; // Guaranteed raster copy for reliable pixel access
        private int _currentCopyIndex = 0;
        private bool _templateReady = false;

        /// <summary>
        /// Escape hatch back to composing a full-page raster per sheet. Printing is the one
        /// thing in this program that must never be a coin toss, so if some driver in the
        /// field mishandles device text there is a way to put a shop back on the old path
        /// without shipping a new build: set APEX_LEGACY_PAGE_RENDER=1.
        /// </summary>
        private static bool LegacyPageRender =>
            Environment.GetEnvironmentVariable("APEX_LEGACY_PAGE_RENDER") == "1";

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

                // Hand the same artwork to the spooler once, so pages that are just numbers
                // over this sheet can be queued as text instead of a full-page raster each.
                _spoolerService.SetSharedBackground(_cachedTemplate);

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
        public async Task StartJobAsync(PrintJobSettings settings, CancellationToken ct)
        {
            await _spoolerService.StartJobAsync(settings, ct);

            // StartJobAsync wipes the spooler's per-job state, and the shared sheet is part
            // of it — so it has to be handed over again AFTER the job opens, not before.
            // Setting it only in SetCachedTemplate left it null by the time pages arrived
            // and every sheet quietly fell back to composing its own full-page raster.
            if (_templateReady && _cachedTemplate != null)
            {
                _spoolerService.SetSharedBackground(_cachedTemplate);
            }
        }

        /// <summary>
        /// Sets the number formatting for this job: digit count, prefix, suffix and whether
        /// to print Arabic-Indic numerals. Call once before the first page.
        /// </summary>
        public void ConfigureNumberFormat(NumberFormatOptions? format, bool useArabicDigits)
        {
            _composer.UseArabicDigits = useArabicDigits;
            _composer.NumberFormat = (format ?? NumberFormatOptions.Default) with
            {
                UseArabicDigits = useArabicDigits
            };
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

            var composer = _composer;

            // Create page assignment from command - FRESH for each page
            var slotAssignments = new List<SlotAssignment>();
            for (int i = 0; i < command.PageNumbers.Length && i < command.Slots.Count; i++)
            {
                slotAssignments.Add(new SlotAssignment(command.Slots[i].Id, command.PageNumbers[i]));
            }

            var pageAssignment = new PageAssignment(0, slotAssignments);

            // ═══════════════════════════════════════════════════════════════════
            // FAST PATH: the sheet is already with the spooler, so send only the
            // numbers and let the press draw them as type at its own resolution.
            // Composing a full-page raster per sheet is what made a 100,000-number
            // run in two copies crawl — 200,000 surfaces of ~35 MB, each PNG-encoded
            // and decoded again — and what left the digits pixelated on a 600 dpi
            // machine. Pages the press cannot draw as plain text (barcodes, QR, or
            // Arabic labels that need shaping) fall through to the composer below.
            // ═══════════════════════════════════════════════════════════════════
            if (_spoolerService.HasSharedBackground && !LegacyPageRender)
            {
                var overlays = composer.TryBuildTextOverlays(pageAssignment, command.Slots, command.CopyType);
                if (overlays != null)
                {
                    await _spoolerService.PrintPageAsync(overlays);
                    return;
                }
            }

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

        // ExtractPageNumbers / ExtractSlots used to live here, rebuilding a page from a
        // PagePrintCommand that no longer carried enough to rebuild it: slot width and
        // height were replaced with 0.1 x 0.05 defaults, alignment forced to Left, rotation
        // dropped to 0, copy styles thrown away and CopyType hard-coded to Original — so
        // every copy printed like the original, and any rotated or right-aligned field
        // printed somewhere other than where it was placed. Worse, the number came back
        // through long.TryParse on already-formatted text, so a series prefix such as
        // "INV-000123" parsed as nothing and the slot was dropped from the sheet entirely.
        // The orchestrator now passes the real slots and copy type straight through.

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

