using Apex.Services.Printing.RIP.Models;
using PdfiumViewer;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.Services.Printing.RIP
{
    /// <summary>
    /// Hybrid Render Strategy - Executes the rendering strategy determined by Decision Engine.
    /// 
    /// RIP-LIKE BEHAVIOR:
    /// - Native Vector: Preserves text and vectors without rasterization
    /// - High DPI Raster: Rasterizes images at required DPI
    /// - Hybrid: Combines native vector with rasterized images
    /// - Fallback: Last resort rasterization
    /// </summary>
    public class HybridRenderStrategy
    {
        /// <summary>
        /// Ceiling on rasterisation resolution.
        ///
        /// Pdfium hands back a 32-bit surface, so A4 costs ~35 MB at 300 dpi and
        /// ~139 MB at 600. A batch can drive several printers at once, each holding
        /// a page plus its clone, so the ceiling keeps a large batch inside memory.
        /// </summary>
        private const int MaxRasterDpi = 300;

        /// <summary>
        /// Rasterises a page at a real resolution.
        ///
        /// PdfiumViewer's Render(page, dpiX, dpiY, flags) overload sizes the bitmap
        /// from the page's dimensions in POINTS and uses the dpi arguments only for
        /// internal correction — so it returns the same 595x841 pixels for A4 whether
        /// you ask for 72 dpi or 600. Passing the pixel size explicitly is the only
        /// way to actually get the requested resolution.
        /// </summary>
        private static Bitmap RenderAtDpi(PdfDocument document, int pageIndex, int requestedDpi)
        {
            int dpi = Math.Clamp(requestedDpi > 0 ? requestedDpi : 300, 72, MaxRasterDpi);

            var sizeInPoints = document.PageSizes[pageIndex];
            int pxWidth = Math.Max(1, (int)Math.Round(sizeInPoints.Width / 72.0 * dpi));
            int pxHeight = Math.Max(1, (int)Math.Round(sizeInPoints.Height / 72.0 * dpi));

            using var rendered = document.Render(
                pageIndex, pxWidth, pxHeight, dpi, dpi, PdfRenderFlags.ForPrinting);

            return new Bitmap(rendered);
        }

        /// <summary>
        /// The resolution a page was actually rasterised at, after clamping.
        /// </summary>
        private static int EffectiveDpi(int requestedDpi) =>
            Math.Clamp(requestedDpi > 0 ? requestedDpi : 300, 72, MaxRasterDpi);

        /// <summary>
        /// Render a page according to the decision.
        /// </summary>
        public async Task<RenderResult> RenderPageAsync(
            string pdfPath,
            int pageIndex,
            RenderDecision decision,
            PageContentProfile profile,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                try
                {
                    switch (decision.Strategy)
                    {
                        case RenderStrategy.NativeVector:
                            return RenderNativeVector(pdfPath, pageIndex, decision, profile);

                        case RenderStrategy.HighDpiRaster:
                            return RenderHighDpiRaster(pdfPath, pageIndex, decision, profile);

                        case RenderStrategy.Hybrid:
                            return RenderHybrid(pdfPath, pageIndex, decision, profile);

                        case RenderStrategy.FallbackRaster:
                            return RenderFallbackRaster(pdfPath, pageIndex, decision, profile);

                        default:
                            throw new NotSupportedException($"Strategy {decision.Strategy} is not supported");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[HybridRender] Error rendering page {pageIndex}: {ex.Message}");
                    // Fallback to safe rasterization
                    return RenderFallbackRaster(pdfPath, pageIndex, decision, profile);
                }
            }, cancellationToken);
        }

        #region Render Strategies

        /// <summary>
        /// Render using native vector (text and vectors preserved).
        /// Best quality - no rasterization.
        /// </summary>
        private RenderResult RenderNativeVector(
            string pdfPath,
            int pageIndex,
            RenderDecision decision,
            PageContentProfile profile)
        {
            // The flag below asks the output generator for native text/vector commands.
            // No generator emits them yet — every one converts a bitmap — so a page
            // rendered without a raster produced zero bytes, which the quality gate
            // rejected, which aborted the whole document. Text-only pages hit this,
            // so a book printed its cover and then died on page two.
            //
            // Carry a raster alongside the flag: a generator that learns to emit
            // native content can still prefer it, and until then there is something
            // to print.
            using var pdfDocument = PdfDocument.Load(pdfPath);
            if (pageIndex < 0 || pageIndex >= pdfDocument.PageCount)
                throw new ArgumentOutOfRangeException(nameof(pageIndex));

            return new RenderResult
            {
                Success = true,
                RenderStrategy = RenderStrategy.NativeVector,
                RequiresNativeOutput = true,
                RasterizedImage = RenderAtDpi(pdfDocument, pageIndex, decision.RequiredDpi),
                PageIndex = pageIndex,
                RenderedDpi = EffectiveDpi(decision.RequiredDpi),
                PreserveText = decision.PreserveText,
                PreserveVectors = decision.PreserveVectors
            };
        }

        /// <summary>
        /// Render using high DPI rasterization (for images only).
        /// </summary>
        private RenderResult RenderHighDpiRaster(
            string pdfPath,
            int pageIndex,
            RenderDecision decision,
            PageContentProfile profile)
        {
            try
            {
                using var pdfDocument = PdfDocument.Load(pdfPath);
                if (pageIndex < 0 || pageIndex >= pdfDocument.PageCount)
                    throw new ArgumentOutOfRangeException(nameof(pageIndex));

                return new RenderResult
                {
                    Success = true,
                    RenderStrategy = RenderStrategy.HighDpiRaster,
                    RequiresNativeOutput = false,
                    RasterizedImage = RenderAtDpi(pdfDocument, pageIndex, decision.RequiredDpi),
                    PageIndex = pageIndex,
                    RenderedDpi = EffectiveDpi(decision.RequiredDpi)
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HybridRender] High DPI raster failed: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Render using hybrid approach (native vector + rasterized images).
        /// </summary>
        private RenderResult RenderHybrid(
            string pdfPath,
            int pageIndex,
            RenderDecision decision,
            PageContentProfile profile)
        {
            // Hybrid rendering:
            // 1. Extract text and vectors (preserve as native)
            // 2. Rasterize images at required DPI
            // 3. Output generator will combine them

            // For now, we render the full page at high DPI
            // In a full implementation, we would:
            // - Extract text/vector content separately
            // - Rasterize only image regions
            // - Combine in output generator

            try
            {
                using var pdfDocument = PdfDocument.Load(pdfPath);
                if (pageIndex < 0 || pageIndex >= pdfDocument.PageCount)
                    throw new ArgumentOutOfRangeException(nameof(pageIndex));

                return new RenderResult
                {
                    Success = true,
                    RenderStrategy = RenderStrategy.Hybrid,
                    RequiresNativeOutput = decision.PreserveText || decision.PreserveVectors,
                    RasterizedImage = RenderAtDpi(pdfDocument, pageIndex, decision.RequiredDpi),
                    PageIndex = pageIndex,
                    RenderedDpi = EffectiveDpi(decision.RequiredDpi),
                    PreserveText = decision.PreserveText,
                    PreserveVectors = decision.PreserveVectors
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HybridRender] Hybrid render failed: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Fallback rasterization (last resort).
        /// </summary>
        private RenderResult RenderFallbackRaster(
            string pdfPath,
            int pageIndex,
            RenderDecision decision,
            PageContentProfile profile)
        {
            try
            {
                using var pdfDocument = PdfDocument.Load(pdfPath);
                if (pageIndex < 0 || pageIndex >= pdfDocument.PageCount)
                    throw new ArgumentOutOfRangeException(nameof(pageIndex));

                return new RenderResult
                {
                    Success = true,
                    RenderStrategy = RenderStrategy.FallbackRaster,
                    RequiresNativeOutput = false,
                    RasterizedImage = RenderAtDpi(pdfDocument, pageIndex, decision.RequiredDpi),
                    PageIndex = pageIndex,
                    RenderedDpi = EffectiveDpi(decision.RequiredDpi)
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HybridRender] Fallback raster failed: {ex.Message}");
                throw;
            }
        }

        #endregion
    }

    /// <summary>
    /// Result of rendering a page.
    /// </summary>
    public class RenderResult
    {
        public bool Success { get; set; }
        public RenderStrategy RenderStrategy { get; set; }
        public int PageIndex { get; set; }
        public bool RequiresNativeOutput { get; set; }
        public Bitmap? RasterizedImage { get; set; }
        public int RenderedDpi { get; set; }
        public bool PreserveText { get; set; }
        public bool PreserveVectors { get; set; }
        public string? ErrorMessage { get; set; }

        public void Dispose()
        {
            RasterizedImage?.Dispose();
            RasterizedImage = null;
        }
    }
}
