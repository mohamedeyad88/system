using System;
using System.Diagnostics;
using SkiaSharp;

namespace Apex.Services.Printing.ConsistentRIP
{
    /// <summary>
    /// HALFTONE CONTROL SYSTEM - Ensures consistent dot patterns and gradients.
    /// 
    /// CRITICAL FOR CONSISTENCY:
    /// - Enforce unified halftone philosophy
    /// - Predictable dot distribution
    /// - Stable gradients
    /// - Do NOT rely on printer default dithering
    /// - Prevent per-driver halftone variation
    /// 
    /// HALFTONE METHODS:
    /// 1. Ordered Dithering (Bayer matrix) - Fast, predictable
    /// 2. Error Diffusion (Floyd-Steinberg) - High quality
    /// 3. Clustered Dot - Traditional printing simulation
    /// 
    /// GOAL:
    /// Same texture perception across all devices.
    /// Smooth gradients without banding.
    /// </summary>
    public class HalftoneController
    {
        private readonly double _screenFrequency; // Lines per inch
        private readonly double _screenAngle;     // Degrees
        private readonly HalftoneMethod _method;
        
        // Bayer matrix for ordered dithering (8x8)
        private static readonly byte[,] BayerMatrix8x8 = new byte[,]
        {
            {  0, 48, 12, 60,  3, 51, 15, 63 },
            { 32, 16, 44, 28, 35, 19, 47, 31 },
            {  8, 56,  4, 52, 11, 59,  7, 55 },
            { 40, 24, 36, 20, 43, 27, 39, 23 },
            {  2, 50, 14, 62,  1, 49, 13, 61 },
            { 34, 18, 46, 30, 33, 17, 45, 29 },
            { 10, 58,  6, 54,  9, 57,  5, 53 },
            { 42, 26, 38, 22, 41, 25, 37, 21 }
        };
        
        public HalftoneController(double screenFrequency, double screenAngle, HalftoneMethod method = HalftoneMethod.OrderedDither)
        {
            _screenFrequency = screenFrequency;
            _screenAngle = screenAngle;
            _method = method;
            
            Debug.WriteLine($"[Halftone] ✓ Halftone Controller initialized");
            Debug.WriteLine($"[Halftone]   Screen frequency: {_screenFrequency} LPI");
            Debug.WriteLine($"[Halftone]   Screen angle: {_screenAngle}°");
            Debug.WriteLine($"[Halftone]   Method: {_method}");
        }
        
        /// <summary>
        /// Applies unified halftone to rasterized document.
        /// </summary>
        public RasterizedDocument ApplyHalftone(RasterizedDocument document)
        {
            Debug.WriteLine($"[Halftone] Applying {_method} halftone to {document.PageCount} pages");
            
            foreach (var page in document.Pages)
            {
                ApplyHalftonePage(page.Bitmap);
            }
            
            return document;
        }
        
        /// <summary>
        /// Applies halftone to a single page bitmap.
        /// </summary>
        private void ApplyHalftonePage(SKBitmap bitmap)
        {
            switch (_method)
            {
                case HalftoneMethod.OrderedDither:
                    ApplyOrderedDithering(bitmap);
                    break;
                    
                case HalftoneMethod.ErrorDiffusion:
                    ApplyErrorDiffusion(bitmap);
                    break;
                    
                case HalftoneMethod.ClusteredDot:
                    ApplyClusteredDot(bitmap);
                    break;
                    
                case HalftoneMethod.None:
                    // No halftone
                    break;
            }
        }
        
        /// <summary>
        /// Applies ordered dithering using Bayer matrix.
        /// FASTEST and most predictable method.
        /// </summary>
        private void ApplyOrderedDithering(SKBitmap bitmap)
        {
            int matrixSize = 8;
            
            unsafe
            {
                var pixels = (SKColor*)bitmap.GetPixels();
                
                for (int y = 0; y < bitmap.Height; y++)
                {
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        int index = y * bitmap.Width + x;
                        var pixel = pixels[index];
                        
                        // Get threshold from Bayer matrix
                        int threshold = BayerMatrix8x8[y % matrixSize, x % matrixSize] * 4;
                        
                        // Apply dithering to each channel
                        byte r = (byte)(pixel.Red > threshold ? 255 : 0);
                        byte g = (byte)(pixel.Green > threshold ? 255 : 0);
                        byte b = (byte)(pixel.Blue > threshold ? 255 : 0);
                        
                        // For grayscale halftoning
                        byte gray = (byte)((pixel.Red + pixel.Green + pixel.Blue) / 3);
                        if (gray > threshold)
                        {
                            pixels[index] = new SKColor(
                                Math.Max(r, pixel.Red),
                                Math.Max(g, pixel.Green),
                                Math.Max(b, pixel.Blue),
                                pixel.Alpha
                            );
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Applies error diffusion (Floyd-Steinberg) dithering.
        /// HIGHEST QUALITY but slower.
        /// </summary>
        private void ApplyErrorDiffusion(SKBitmap bitmap)
        {
            unsafe
            {
                var pixels = (SKColor*)bitmap.GetPixels();
                float[,] errors = new float[bitmap.Height, bitmap.Width];
                
                for (int y = 0; y < bitmap.Height; y++)
                {
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        int index = y * bitmap.Width + x;
                        var pixel = pixels[index];
                        
                        // Convert to grayscale + accumulated error
                        float gray = (pixel.Red + pixel.Green + pixel.Blue) / 3f;
                        gray = Math.Clamp(gray + errors[y, x], 0, 255);
                        
                        // Quantize
                        byte newGray = (byte)(gray > 128 ? 255 : 0);
                        float error = gray - newGray;
                        
                        // Distribute error to neighbors (Floyd-Steinberg)
                        if (x + 1 < bitmap.Width)
                            errors[y, x + 1] += error * 7f / 16f;
                        
                        if (y + 1 < bitmap.Height)
                        {
                            if (x > 0)
                                errors[y + 1, x - 1] += error * 3f / 16f;
                            
                            errors[y + 1, x] += error * 5f / 16f;
                            
                            if (x + 1 < bitmap.Width)
                                errors[y + 1, x + 1] += error * 1f / 16f;
                        }
                        
                        pixels[index] = new SKColor(newGray, newGray, newGray, pixel.Alpha);
                    }
                }
            }
        }
        
        /// <summary>
        /// Applies clustered dot halftoning.
        /// Simulates traditional printing dots.
        /// </summary>
        private void ApplyClusteredDot(SKBitmap bitmap)
        {
            // Simplified clustered dot using rotated threshold matrix
            int cellSize = 4;
            
            unsafe
            {
                var pixels = (SKColor*)bitmap.GetPixels();
                
                for (int y = 0; y < bitmap.Height; y += cellSize)
                {
                    for (int x = 0; x < bitmap.Width; x += cellSize)
                    {
                        // Calculate average gray level in cell
                        float avgGray = 0;
                        int count = 0;
                        
                        for (int dy = 0; dy < cellSize && y + dy < bitmap.Height; dy++)
                        {
                            for (int dx = 0; dx < cellSize && x + dx < bitmap.Width; dx++)
                            {
                                var p = pixels[(y + dy) * bitmap.Width + (x + dx)];
                                avgGray += (p.Red + p.Green + p.Blue) / 3f;
                                count++;
                            }
                        }
                        
                        avgGray /= count;
                        
                        // Determine dot size based on gray level
                        int dotSize = (int)(avgGray / 255f * cellSize);
                        
                        // Draw clustered dot
                        for (int dy = 0; dy < dotSize && y + dy < bitmap.Height; dy++)
                        {
                            for (int dx = 0; dx < dotSize && x + dx < bitmap.Width; dx++)
                            {
                                pixels[(y + dy) * bitmap.Width + (x + dx)] = SKColors.Black;
                            }
                        }
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Halftone methods.
    /// </summary>
    public enum HalftoneMethod
    {
        /// <summary>No halftoning (use printer default).</summary>
        None,
        
        /// <summary>Ordered dithering with Bayer matrix (fast, predictable).</summary>
        OrderedDither,
        
        /// <summary>Error diffusion / Floyd-Steinberg (highest quality).</summary>
        ErrorDiffusion,
        
        /// <summary>Clustered dot halftoning (traditional printing simulation).</summary>
        ClusteredDot
    }
}
