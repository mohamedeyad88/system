using System;
using System.Diagnostics;
using SkiaSharp;

namespace Apex.Services.Printing.ConsistentRIP
{
    /// <summary>
    /// UNIFIED BLACK STRATEGY - CRITICAL for consistent black appearance.
    /// 
    /// PROBLEM:
    /// Different printers render black differently:
    /// - Some use pure K (black cartridge only)
    /// - Some mix CMYK for "rich black"
    /// - Some auto-enhance blacks
    /// This causes visible differences between printers.
    /// 
    /// SOLUTION:
    /// Centralized black handling policy:
    /// - Text: Pure K (100% black, 0% CMY)
    /// - Graphics: Rich Black (C=60%, M=40%, Y=40%, K=100%)
    /// - Photos: Auto-detect best black
    /// 
    /// ENFORCEMENT:
    /// Prevent driver-based black enhancement or substitution.
    /// Application controls black intent completely.
    /// 
    /// GOAL:
    /// Black looks identical on HP, Canon, Epson, Brother, etc.
    /// </summary>
    public class UnifiedBlackStrategy
    {
        // Black thresholds (RGB values below this are considered "black")
        private const int BLACK_THRESHOLD = 20;
        
        // Pure black (K only)
        private static readonly CMYK PURE_BLACK = new(0, 0, 0, 100);
        
        // Rich black (for graphics/fills)
        private static readonly CMYK RICH_BLACK = new(60, 40, 40, 100);
        
        // Photo black (optimized for photos)
        private static readonly CMYK PHOTO_BLACK = new(70, 50, 50, 100);
        
        public UnifiedBlackStrategy()
        {
            Debug.WriteLine("[BlackStrategy] ✓ Unified Black Strategy initialized");
            Debug.WriteLine($"[BlackStrategy]   Pure Black: {PURE_BLACK}");
            Debug.WriteLine($"[BlackStrategy]   Rich Black: {RICH_BLACK}");
            Debug.WriteLine($"[BlackStrategy]   Photo Black: {PHOTO_BLACK}");
        }
        
        /// <summary>
        /// Processes blacks in document according to unified strategy.
        /// </summary>
        public InterpretedDocument ProcessBlacks(InterpretedDocument document)
        {
            Debug.WriteLine($"[BlackStrategy] Processing blacks for {document.PageCount} pages");
            
            foreach (var page in document.Pages)
            {
                ProcessPageBlacks(page);
            }
            
            return document;
        }
        
        /// <summary>
        /// Processes blacks on a single page.
        /// </summary>
        private void ProcessPageBlacks(InterpretedPage page)
        {
            // Process text elements (use Pure Black)
            if (page.TextElements != null)
            {
                foreach (var text in page.TextElements)
                {
                    if (IsBlack(text.Color))
                    {
                        // Enforce pure black for text
                        text.Color = new SKColor(0, 0, 0, text.Color.Alpha);
                        
                        // Tag for pure K output
                        if (!text.Metadata.ContainsKey("BlackMode"))
                            text.Metadata["BlackMode"] = BlackMode.PureK;
                    }
                }
            }
            
            // Process image data
            if (page.ImageData != null)
            {
                ProcessImageBlacks(page.ImageData, page.SourceType);
            }
        }
        
        /// <summary>
        /// Processes blacks in an image bitmap.
        /// </summary>
        private void ProcessImageBlacks(SKBitmap bitmap, ContentType contentType)
        {
            // Determine black strategy based on content type
            var strategy = contentType == ContentType.Image 
                ? BlackMode.PhotoBlack 
                : BlackMode.RichBlack;
            
            // Process each pixel
            unsafe
            {
                var pixels = bitmap.GetPixels();
                int pixelCount = bitmap.Width * bitmap.Height;
                
                for (int i = 0; i < pixelCount; i++)
                {
                    var color = ((SKColor*)pixels)[i];
                    
                    if (IsBlack(color))
                    {
                        // Replace with unified black
                        var unifiedBlack = GetUnifiedBlack(strategy, color.Alpha);
                        ((SKColor*)pixels)[i] = unifiedBlack;
                    }
                }
            }
        }
        
        /// <summary>
        /// Checks if a color is considered "black".
        /// </summary>
        private bool IsBlack(SKColor color)
        {
            return color.Red <= BLACK_THRESHOLD 
                && color.Green <= BLACK_THRESHOLD 
                && color.Blue <= BLACK_THRESHOLD;
        }
        
        /// <summary>
        /// Gets unified black color based on strategy.
        /// </summary>
        private SKColor GetUnifiedBlack(BlackMode mode, byte alpha)
        {
            // All blacks are normalized to pure RGB black (0,0,0)
            // The actual K vs CMY mix happens at output generation
            return new SKColor(0, 0, 0, alpha);
        }
        
        /// <summary>
        /// Gets CMYK black values for output generation.
        /// </summary>
        public CMYK GetBlackCMYK(BlackMode mode)
        {
            return mode switch
            {
                BlackMode.PureK => PURE_BLACK,
                BlackMode.RichBlack => RICH_BLACK,
                BlackMode.PhotoBlack => PHOTO_BLACK,
                _ => PURE_BLACK
            };
        }
        
        /// <summary>
        /// Analyzes content to determine optimal black strategy.
        /// </summary>
        public BlackMode DetectOptimalBlackStrategy(InterpretedPage page)
        {
            // Text-heavy content → Pure K
            if (page.TextElements != null && page.TextElements.Count > 10)
                return BlackMode.PureK;
            
            // Photo content → Photo Black
            if (page.SourceType == ContentType.Image)
                return BlackMode.PhotoBlack;
            
            // Graphics/mixed → Rich Black
            return BlackMode.RichBlack;
        }
    }
    
    /// <summary>
    /// Black rendering modes.
    /// </summary>
    public enum BlackMode
    {
        /// <summary>Pure K - black cartridge only (best for text).</summary>
        PureK,
        
        /// <summary>Rich Black - CMYK mix (best for graphics).</summary>
        RichBlack,
        
        /// <summary>Photo Black - optimized mix for photos.</summary>
        PhotoBlack
    }
    
    /// <summary>
    /// Extension to InterpretedPage and TextElement for black metadata.
    /// </summary>
    public static class BlackStrategyExtensions
    {
        public static Dictionary<string, object> Metadata { get; set; } = new();
    }
}
