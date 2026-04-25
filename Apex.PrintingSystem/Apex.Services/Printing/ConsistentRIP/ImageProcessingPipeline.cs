using System;
using System.Diagnostics;
using SkiaSharp;

namespace Apex.Services.Printing.ConsistentRIP
{
    /// <summary>
    /// UNIFIED IMAGE PROCESSING PIPELINE - Ensures consistent image appearance.
    /// 
    /// CRITICAL DESIGN:
    /// - Apply internal, CONSISTENT:
    ///   * Sharpening
    ///   * Noise handling
    ///   * Edge control
    /// - DISABLE all printer-side image enhancement
    /// - Avoid adaptive or auto-enhance behaviors
    /// - Images must look intentionally processed, not device-interpreted
    /// 
    /// GOAL:
    /// Same image quality and appearance across all printers.
    /// </summary>
    public class ImageProcessingPipeline
    {
        // UNIFIED IMAGE PROCESSING PARAMETERS
        private const float UNIFIED_SHARPNESS = 1.2f;        // Moderate sharpening
        private const float UNIFIED_CONTRAST = 1.05f;        // Slight contrast boost
        private const float UNIFIED_SATURATION = 1.0f;       // Neutral saturation
        private const float UNIFIED_BRIGHTNESS = 1.0f;       // No brightness change
        private const bool ENABLE_NOISE_REDUCTION = true;
        private const bool ENABLE_EDGE_ENHANCEMENT = true;
        
        public ImageProcessingPipeline()
        {
            Debug.WriteLine("[ImageProcessor] ✓ Image Processing Pipeline initialized");
            Debug.WriteLine($"[ImageProcessor]   Sharpness: {UNIFIED_SHARPNESS}");
            Debug.WriteLine($"[ImageProcessor]   Contrast: {UNIFIED_CONTRAST}");
            Debug.WriteLine($"[ImageProcessor]   Saturation: {UNIFIED_SATURATION}");
        }
        
        /// <summary>
        /// Processes all images in document with unified pipeline.
        /// </summary>
        public InterpretedDocument ProcessImages(InterpretedDocument document)
        {
            Debug.WriteLine($"[ImageProcessor] Processing images in {document.PageCount} pages");
            
            foreach (var page in document.Pages)
            {
                if (page.ImageData != null)
                {
                    ProcessImage(page.ImageData);
                }
            }
            
            return document;
        }
        
        /// <summary>
        /// Processes a single image with all enhancement steps.
        /// </summary>
        private void ProcessImage(SKBitmap bitmap)
        {
            // Step 1: Noise Reduction (if enabled)
            if (ENABLE_NOISE_REDUCTION)
            {
                ApplyNoiseReduction(bitmap);
            }
            
            // Step 2: Sharpening
            ApplyUnifiedSharpening(bitmap, UNIFIED_SHARPNESS);
            
            // Step 3: Contrast/Saturation/Brightness
            ApplyColorAdjustments(bitmap, UNIFIED_CONTRAST, UNIFIED_SATURATION, UNIFIED_BRIGHTNESS);
            
            // Step 4: Edge Enhancement (if enabled)
            if (ENABLE_EDGE_ENHANCEMENT)
            {
                ApplyEdgeEnhancement(bitmap);
            }
        }
        
        /// <summary>
        /// Applies unified sharpening using convolution matrix.
        /// Same sharpening algorithm for ALL printers.
        /// </summary>
        private void ApplyUnifiedSharpening(SKBitmap bitmap, float strength)
        {
            if (strength <= 0) return;
            
            // Unified sharpening kernel (Laplacian-based)
            // Same kernel applied to all images on all printers
            float[] kernel = new[]
            {
                 0f,           -strength,      0f,
                -strength,     1f + 4*strength, -strength,
                 0f,           -strength,      0f
            };
            
            ApplyConvolution(bitmap, kernel, 3, 3);
        }
        
        /// <summary>
        /// Applies color adjustments (contrast, saturation, brightness).
        /// </summary>
        private void ApplyColorAdjustments(SKBitmap bitmap, float contrast, float saturation, float brightness)
        {
            // Build unified color matrix
            float[] matrix = new float[20];
            
            // Contrast/Saturation/Brightness matrix
            float c = contrast;
            float s = saturation;
            float b = brightness;
            
            matrix[0] = c * s;   matrix[1] = 0;       matrix[2] = 0;       matrix[3] = 0;  matrix[4] = b;
            matrix[5] = 0;       matrix[6] = c * s;   matrix[7] = 0;       matrix[8] = 0;  matrix[9] = b;
            matrix[10] = 0;      matrix[11] = 0;      matrix[12] = c * s;  matrix[13] = 0; matrix[14] = b;
            matrix[15] = 0;      matrix[16] = 0;      matrix[17] = 0;      matrix[18] = 1; matrix[19] = 0;
            
            using var canvas = new SKCanvas(bitmap);
            using var paint = new SKPaint();
            paint.ColorFilter = SKColorFilter.CreateColorMatrix(matrix);
            
            canvas.DrawBitmap(bitmap, 0, 0, paint);
        }
        
        /// <summary>
        /// Applies noise reduction for cleaner output.
        /// </summary>
        private void ApplyNoiseReduction(SKBitmap bitmap)
        {
            // Simple Gaussian-like blur for noise reduction
            float[] kernel = new[]
            {
                1f/16f, 2f/16f, 1f/16f,
                2f/16f, 4f/16f, 2f/16f,
                1f/16f, 2f/16f, 1f/16f
            };
            
            ApplyConvolution(bitmap, kernel, 3, 3);
        }
        
        /// <summary>
        /// Applies edge enhancement for sharper edges.
        /// </summary>
        private void ApplyEdgeEnhancement(SKBitmap bitmap)
        {
            // Edge detection kernel
            float[] kernel = new[]
            {
                -1f, -1f, -1f,
                -1f,  9f, -1f,
                -1f, -1f, -1f
            };
            
            ApplyConvolution(bitmap, kernel, 3, 3, 0.3f); // Subtle edge enhancement
        }
        
        /// <summary>
        /// Applies convolution kernel to bitmap.
        /// UNIFIED algorithm for all printers.
        /// </summary>
        private void ApplyConvolution(SKBitmap bitmap, float[] kernel, int kernelWidth, int kernelHeight, float strength = 1.0f)
        {
            if (bitmap == null || kernel == null)
                return;
            
            var result = new SKBitmap(bitmap.Width, bitmap.Height, bitmap.ColorType, bitmap.AlphaType);
            
            int halfWidth = kernelWidth / 2;
            int halfHeight = kernelHeight / 2;
            
            unsafe
            {
                var srcPixels = (SKColor*)bitmap.GetPixels();
                var dstPixels = (SKColor*)result.GetPixels();
                
                for (int y = halfHeight; y < bitmap.Height - halfHeight; y++)
                {
                    for (int x = halfWidth; x < bitmap.Width - halfWidth; x++)
                    {
                        float r = 0, g = 0, b = 0;
                        
                        // Apply kernel
                        for (int ky = 0; ky < kernelHeight; ky++)
                        {
                            for (int kx = 0; kx < kernelWidth; kx++)
                            {
                                int px = x + kx - halfWidth;
                                int py = y + ky - halfHeight;
                                var pixel = srcPixels[py * bitmap.Width + px];
                                var weight = kernel[ky * kernelWidth + kx] * strength;
                                
                                r += pixel.Red * weight;
                                g += pixel.Green * weight;
                                b += pixel.Blue * weight;
                            }
                        }
                        
                        // Get original pixel for blending
                        var original = srcPixels[y * bitmap.Width + x];
                        
                        // Blend processed with original
                        r = r * strength + original.Red * (1 - strength);
                        g = g * strength + original.Green * (1 - strength);
                        b = b * strength + original.Blue * (1 - strength);
                        
                        dstPixels[y * bitmap.Width + x] = new SKColor(
                            (byte)Math.Clamp(r, 0, 255),
                            (byte)Math.Clamp(g, 0, 255),
                            (byte)Math.Clamp(b, 0, 255),
                            original.Alpha
                        );
                    }
                }
            }
            
            // Copy result back to original
            result.CopyTo(bitmap);
            result.Dispose();
        }
        
        /// <summary>
        /// Rasterizes vector content with unified anti-aliasing.
        /// </summary>
        // Removed - moved to UnifiedRasterizationEngine
    }
}
