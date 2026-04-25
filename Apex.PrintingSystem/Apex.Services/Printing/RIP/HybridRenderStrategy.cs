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
            // For native vector, we preserve the PDF content as-is
            // The output generator will convert to PostScript/PCL with native text/vector commands
            return new RenderResult
            {
                Success = true,
                RenderStrategy = RenderStrategy.NativeVector,
                RequiresNativeOutput = true,
                RasterizedImage = null,
                PageIndex = pageIndex
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

                // Render at required DPI
                int dpi = decision.RequiredDpi;
                using var rendered = pdfDocument.Render(
                    pageIndex, 
                    dpi, 
                    dpi, 
                    PdfRenderFlags.ForPrinting);

                return new RenderResult
                {
                    Success = true,
                    RenderStrategy = RenderStrategy.HighDpiRaster,
                    RequiresNativeOutput = false,
                    RasterizedImage = new Bitmap(rendered), // Clone for use
                    PageIndex = pageIndex,
                    RenderedDpi = dpi
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

                // Render at required DPI for images
                int dpi = decision.RequiredDpi;
                using var rendered = pdfDocument.Render(
                    pageIndex,
                    dpi,
                    dpi,
                    PdfRenderFlags.ForPrinting);

                return new RenderResult
                {
                    Success = true,
                    RenderStrategy = RenderStrategy.Hybrid,
                    RequiresNativeOutput = decision.PreserveText || decision.PreserveVectors,
                    RasterizedImage = new Bitmap(rendered),
                    PageIndex = pageIndex,
                    RenderedDpi = dpi,
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

                // Fallback: render at standard DPI
                int dpi = decision.RequiredDpi > 0 ? decision.RequiredDpi : 300;
                using var rendered = pdfDocument.Render(
                    pageIndex,
                    dpi,
                    dpi,
                    PdfRenderFlags.ForPrinting);

                return new RenderResult
                {
                    Success = true,
                    RenderStrategy = RenderStrategy.FallbackRaster,
                    RequiresNativeOutput = false,
                    RasterizedImage = new Bitmap(rendered),
                    PageIndex = pageIndex,
                    RenderedDpi = dpi
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
