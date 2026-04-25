using System;
using System.Diagnostics;
using SkiaSharp;

namespace Apex.Services.Printing.ConsistentRIP
{
    /// <summary>
    /// RESOLUTION NORMALIZATION SYSTEM - Ensures consistent scale across devices.
    /// 
    /// CRITICAL PRINCIPLE:
    /// "Same structure, different execution density"
    /// 
    /// DESIGN:
    /// - Normalize ALL content to unified internal DPI (600 by default)
    /// - Higher-DPI printers receive SCALED data, not raw instructions
    /// - Lower-DPI printers receive optimized data without reprocessing
    /// - Printers NEVER re-interpret page geometry
    /// 
    /// PREVENTS:
    /// - Different scaling on different printers
    /// - Printer-side resolution adaptation
    /// - Geometry inconsistencies
    /// 
    /// GOAL:
    /// Same visual structure on all printers, regardless of native DPI.
    /// </summary>
    public class ResolutionNormalizer
    {
        private readonly int _unifiedDPI;
        
        public ResolutionNormalizer(int unifiedDPI)
        {
            _unifiedDPI = unifiedDPI;
            
            Debug.WriteLine($"[ResolutionNorm] ✓ Resolution Normalizer initialized at {_unifiedDPI} DPI");
        }
        
        /// <summary>
        /// Normalizes document content to unified DPI.
        /// </summary>
        public InterpretedDocument NormalizeContent(InterpretedDocument document)
        {
            Debug.WriteLine($"[ResolutionNorm] Normalizing {document.PageCount} pages to {_unifiedDPI} DPI");
            
            foreach (var page in document.Pages)
            {
                NormalizePage(page);
            }
            
            return document;
        }
        
        /// <summary>
        /// Normalizes a single page to unified DPI.
        /// </summary>
        private void NormalizePage(InterpretedPage page)
        {
            // Calculate normalized dimensions
            float widthInInches = page.WidthInPoints / 72f;  // 72 points = 1 inch
            float heightInInches = page.HeightInPoints / 72f;
            
            int normalizedWidth = (int)(widthInInches * _unifiedDPI);
            int normalizedHeight = (int)(heightInInches * _unifiedDPI);
            
            // Store normalized dimensions
            page.Metadata["NormalizedWidth"] = normalizedWidth;
            page.Metadata["NormalizedHeight"] = normalizedHeight;
            page.Metadata["UnifiedDPI"] = _unifiedDPI;
            
            // Normalize image data if present
            if (page.ImageData != null)
            {
                page.ImageData = NormalizeBitmap(page.ImageData, normalizedWidth, normalizedHeight);
            }
            
            Debug.WriteLine($"[ResolutionNorm] Page {page.PageNumber}: {normalizedWidth}x{normalizedHeight}px @ {_unifiedDPI} DPI");
        }
        
        /// <summary>
        /// Normalizes bitmap to target dimensions using high-quality scaling.
        /// Uses unified interpolation algorithm for consistency.
        /// </summary>
        private SKBitmap NormalizeBitmap(SKBitmap source, int targetWidth, int targetHeight)
        {
            if (source.Width == targetWidth && source.Height == targetHeight)
                return source; // Already normalized
            
            // Create target bitmap
            var normalized = new SKBitmap(targetWidth, targetHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            
            using var canvas = new SKCanvas(normalized);
            using var paint = new SKPaint
            {
                // CRITICAL: Use HIGH-QUALITY interpolation for consistency
                FilterQuality = SKFilterQuality.High,
                IsAntialias = true
            };
            
            // Scale to fill target dimensions
            var destRect = new SKRect(0, 0, targetWidth, targetHeight);
            canvas.DrawBitmap(source, destRect, paint);
            
            return normalized;
        }
        
        /// <summary>
        /// Calculates scaling factor for a specific printer.
        /// </summary>
        public float CalculateScalingFactor(PrinterProfile printer)
        {
            return (float)printer.NativeDPI / _unifiedDPI;
        }
        
        /// <summary>
        /// Adapts normalized content to printer's native DPI.
        /// IMPORTANT: This is SCALING, not re-interpretation.
        /// </summary>
        public SKBitmap AdaptToTargetDPI(SKBitmap normalizedBitmap, PrinterProfile printer)
        {
            float scale = CalculateScalingFactor(printer);
            
            if (Math.Abs(scale - 1.0f) < 0.01f)
                return normalizedBitmap; // No scaling needed
            
            int targetWidth = (int)(normalizedBitmap.Width * scale);
            int targetHeight = (int)(normalizedBitmap.Height * scale);
            
            Debug.WriteLine($"[ResolutionNorm] Adapting to {printer.NativeDPI} DPI: {normalizedBitmap.Width}x{normalizedBitmap.Height} → {targetWidth}x{targetHeight}");
            
            var adapted = new SKBitmap(targetWidth, targetHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            
            using var canvas = new SKCanvas(adapted);
            using var paint = new SKPaint
            {
                // CRITICAL: Same high-quality filter for all printers
                FilterQuality = SKFilterQuality.High,
                IsAntialias = true
            };
            
            canvas.DrawBitmap(normalizedBitmap, new SKRect(0, 0, targetWidth, targetHeight), paint);
            
            return adapted;
        }
    }
}
