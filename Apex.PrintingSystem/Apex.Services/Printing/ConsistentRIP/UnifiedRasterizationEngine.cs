using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using PdfiumViewer;
using SkiaSharp;

namespace Apex.Services.Printing.ConsistentRIP
{
    /// <summary>
    /// UNIFIED RASTERIZATION ENGINE - The heart of consistent rendering.
    /// 
    /// CRITICAL DESIGN:
    /// - Perform rasterization INSIDE the application
    /// - Use FIXED internal DPI (600 by default)
    /// - Enforce IDENTICAL scaling logic across all printers
    /// - Enforce IDENTICAL interpolation algorithms
    /// - Enforce IDENTICAL anti-aliasing rules
    /// - Printer must NEVER re-interpret page geometry
    /// 
    /// PREVENTS:
    /// - Driver-specific rasterization
    /// - Printer-specific scaling
    /// - Variable quality settings
    /// - Inconsistent anti-aliasing
    /// 
    /// GOAL:
    /// Pixel-perfect consistency in rasterization across all devices.
    /// </summary>
    public class UnifiedRasterizationEngine
    {
        private readonly int _internalDPI;
        
        // UNIFIED RENDERING SETTINGS (applied to ALL printers)
        private const SKFilterQuality UNIFIED_FILTER_QUALITY = SKFilterQuality.High;
        private const bool UNIFIED_ANTIALIAS = true;
        private const bool UNIFIED_DITHER = true;
        private const PdfRenderFlags UNIFIED_PDF_FLAGS = PdfRenderFlags.Annotations | PdfRenderFlags.ForPrinting;
        
        public UnifiedRasterizationEngine(int internalDPI)
        {
            _internalDPI = internalDPI;
            
            Debug.WriteLine($"[Rasterizer] ✓ Unified Rasterization Engine initialized @ {_internalDPI} DPI");
            Debug.WriteLine($"[Rasterizer]   Filter Quality: {UNIFIED_FILTER_QUALITY}");
            Debug.WriteLine($"[Rasterizer]   Anti-alias: {UNIFIED_ANTIALIAS}");
            Debug.WriteLine($"[Rasterizer]   Dither: {UNIFIED_DITHER}");
        }
        
        /// <summary>
        /// Rasterizes document using unified settings.
        /// </summary>
        public async Task<RasterizedDocument> RasterizeAsync(
            InterpretedDocument document,
            CancellationToken cancellationToken = default)
        {
            Debug.WriteLine($"[Rasterizer] Rasterizing {document.PageCount} pages @ {_internalDPI} DPI");
            
            var rasterized = new RasterizedDocument
            {
                SourceFile = document.SourceFile,
                PageCount = document.PageCount,
                DPI = _internalDPI
            };
            
            foreach (var page in document.Pages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var rasterizedPage = await RasterizePageAsync(page, document.SourceFormat, cancellationToken);
                rasterized.Pages.Add(rasterizedPage);
            }
            
            Debug.WriteLine($"[Rasterizer] ✓ Rasterization complete: {rasterized.Pages.Count} pages");
            
            return rasterized;
        }
        
        /// <summary>
        /// Rasterizes a single page using unified settings.
        /// </summary>
        private async Task<RasterizedPage> RasterizePageAsync(
            InterpretedPage page,
            FileFormat sourceFormat,
            CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                SKBitmap? bitmap = null;
                Debug.WriteLine($"[Rasterizer] Page {page.PageNumber}: start rasterization ({sourceFormat})");
                
                // Get normalized dimensions
                int width = (int)(page.Metadata["NormalizedWidth"] ?? 
                    (page.WidthInPoints / 72f * _internalDPI));
                int height = (int)(page.Metadata["NormalizedHeight"] ?? 
                    (page.HeightInPoints / 72f * _internalDPI));
                
                if (sourceFormat == FileFormat.PDF)
                {
                    // Rasterize PDF page
                    bitmap = RasterizePdfPage(page, width, height);
                }
                else if (page.ImageData != null)
                {
                    // Use pre-loaded image data (already normalized)
                    bitmap = page.ImageData;
                }
                else if (page.VectorData != null)
                {
                    // Rasterize vector data
                    bitmap = RasterizeVectorData(page.VectorData, width, height);
                }
                else
                {
                    throw new InvalidOperationException("No renderable content in page");
                }
                
                if (bitmap == null)
                {
                    throw new InvalidOperationException($"حدث خطأ أثناء تجهيز الصفحة للطباعة (Page {page.PageNumber}). يرجى التأكد من القالب والطابعة.");
                }

                var rasterizedPage = new RasterizedPage
                {
                    PageNumber = page.PageNumber,
                    Bitmap = bitmap,
                    WidthPixels = bitmap.Width,
                    HeightPixels = bitmap.Height,
                    DPI = _internalDPI
                };
                
                Debug.WriteLine($"[Rasterizer] Page {page.PageNumber}: rasterization complete => {bitmap.Width}x{bitmap.Height}");
                return rasterizedPage;
            }, cancellationToken);
        }
        
        /// <summary>
        /// Rasterizes PDF page using unified settings.
        /// 
        /// CRITICAL FIX: PDF is now PRE-RENDERED in FileInterpretationLayer.
        /// This method just scales/processes the existing bitmap.
        /// 
        /// NO PDF DOCUMENT ACCESS - eliminates ObjectDisposedException bug.
        /// </summary>
        private SKBitmap RasterizePdfPage(InterpretedPage page, int width, int height)
        {
            // CRITICAL FIX: PDF already rendered in FileInterpretationLayer
            // No PDF document reference exists anymore (disposed after interpretation)
            
            if (page.ImageData == null)
                throw new InvalidOperationException(
                    "PDF page not pre-rendered. This indicates a bug in FileInterpretationLayer.");
            
            // If target size matches source, return as-is
            if (page.ImageData.Width == width && page.ImageData.Height == height)
            {
                Debug.WriteLine($"[Rasterizer]   Page {page.PageNumber}: Using pre-rendered bitmap (exact match)");
                return page.ImageData;
            }
            
            // Scale to target DPI if needed
            Debug.WriteLine($"[Rasterizer]   Page {page.PageNumber}: Scaling from {page.ImageData.Width}x{page.ImageData.Height} to {width}x{height}");
            
            var scaled = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            
            using var canvas = new SKCanvas(scaled);
            using var paint = new SKPaint
            {
                FilterQuality = UNIFIED_FILTER_QUALITY,
                IsAntialias = UNIFIED_ANTIALIAS,
                IsDither = UNIFIED_DITHER
            };
            
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(page.ImageData, new SKRect(0, 0, width, height), paint);
            
            return scaled;
        }
        
        /// <summary>
        /// Rasterizes vector data (SKPicture).
        /// </summary>
        private SKBitmap RasterizeVectorData(SKPicture vector, int width, int height)
        {
            var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            
            using var canvas = new SKCanvas(bitmap);
            using var paint = new SKPaint
            {
                FilterQuality = UNIFIED_FILTER_QUALITY,
                IsAntialias = UNIFIED_ANTIALIAS,
                IsDither = UNIFIED_DITHER
            };
            
            // Fill with white background
            canvas.Clear(SKColors.White);
            
            // Draw vector at target resolution
            var scaleX = (float)width / vector.CullRect.Width;
            var scaleY = (float)height / vector.CullRect.Height;
            var scale = Math.Min(scaleX, scaleY);
            
            canvas.Scale(scale);
            canvas.DrawPicture(vector, paint);
            
            return bitmap;
        }
        
        /// <summary>
        /// Converts System.Drawing.Bitmap to SKBitmap.
        /// </summary>
        private SKBitmap ConvertToSKBitmap(System.Drawing.Bitmap gdiBitmap)
        {
            var skBitmap = new SKBitmap(gdiBitmap.Width, gdiBitmap.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            
            using var pixmap = skBitmap.PeekPixels();
            if (pixmap == null)
            {
                skBitmap.Dispose();
                throw new InvalidOperationException("Failed to create pixmap for converted bitmap (PeekPixels returned null).");
            }
            
            // Copy pixel data
            var bitmapData = gdiBitmap.LockBits(
                new System.Drawing.Rectangle(0, 0, gdiBitmap.Width, gdiBitmap.Height),
                System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb
            );
            
            try
            {
                unsafe
                {
                    byte* src = (byte*)bitmapData.Scan0;
                    byte* dst = (byte*)pixmap.GetPixels();
                    int bytes = bitmapData.Stride * bitmapData.Height;
                    
                    for (int i = 0; i < bytes; i += 4)
                    {
                        // Convert BGRA to RGBA
                        dst[i + 0] = src[i + 2]; // R
                        dst[i + 1] = src[i + 1]; // G
                        dst[i + 2] = src[i + 0]; // B
                        dst[i + 3] = src[i + 3]; // A
                    }
                }
            }
            finally
            {
                gdiBitmap.UnlockBits(bitmapData);
            }
            
            return skBitmap;
        }
    }
    
    /// <summary>
    /// Rasterized document - bitmap representation at unified DPI.
    /// CRITICAL: Implements IDisposable to prevent memory leaks from SKBitmap objects.
    /// </summary>
    public class RasterizedDocument : IDisposable
    {
        public string SourceFile { get; set; } = string.Empty;
        public int PageCount { get; set; }
        public int DPI { get; set; }
        public List<RasterizedPage> Pages { get; set; } = new();
        
        private bool _disposed;
        
        /// <summary>
        /// Disposes all bitmap resources to prevent memory leaks.
        /// CRITICAL: Must be called after output generation.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            
            // Dispose all SKBitmap objects (can be 100+ MB each)
            foreach (var page in Pages)
            {
                page.Bitmap?.Dispose();
            }
            
            Pages.Clear();
            _disposed = true;
            
            Debug.WriteLine($"[RasterizedDocument] Disposed {Pages.Count} page bitmaps");
        }
    }
    
    /// <summary>
    /// Rasterized page - single page as bitmap at unified DPI.
    /// </summary>
    public class RasterizedPage
    {
        public int PageNumber { get; set; }
        public SKBitmap Bitmap { get; set; } = null!;
        public int WidthPixels { get; set; }
        public int HeightPixels { get; set; }
        public int DPI { get; set; }
    }
}
