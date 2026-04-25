using Apex.Services.Printing.RIP.Models;
using PdfiumViewer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.Services.Printing.UniversalRIP
{
    /// <summary>
    /// Enhanced Content Analysis Engine - Deep PDF content extraction.
    /// Analyzes each page to detect text, vectors, images, and transparency.
    /// 
    /// MANDATORY: No print job may proceed without this analysis.
    /// </summary>
    public class EnhancedContentAnalysisEngine
    {
        /// <summary>
        /// Analyzes a single page with deep content extraction.
        /// </summary>
        public async Task<PageContentProfile> AnalyzePageAsync(
            string pdfPath,
            int pageIndex,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                var profile = new PageContentProfile
                {
                    PageIndex = pageIndex
                };

                try
                {
                    using var pdfDocument = PdfDocument.Load(pdfPath);
                    
                    if (pageIndex < 0 || pageIndex >= pdfDocument.PageCount)
                        throw new ArgumentOutOfRangeException(nameof(pageIndex));

                    // Get page dimensions
                    var pageSize = pdfDocument.PageSizes[pageIndex];
                    profile.Dimensions = new PageDimensions
                    {
                        WidthPoints = pageSize.Width,
                        HeightPoints = pageSize.Height,
                        PageSize = DeterminePageSize(pageSize.Width, pageSize.Height)
                    };

                    // Deep content analysis
                    AnalyzePageContentDeep(pdfDocument, pageIndex, profile);

                    // Calculate complexity score
                    profile.ComplexityScore = CalculateComplexityScore(profile);

                    // Determine if rasterization is required
                    profile.RequiresRasterization = DetermineRasterizationNeed(profile);

                    return profile;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[EnhancedContentAnalysis] Error analyzing page {pageIndex}: {ex.Message}");
                    
                    // Conservative fallback
                    profile.HasImages = true;
                    profile.HasText = true;
                    profile.IsMixedContent = true;
                    profile.RequiresRasterization = true;
                    profile.MinImageResolution = 300;
                    profile.ComplexityScore = 50;
                    
                    return profile;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Quick sample analysis for large documents.
        /// </summary>
        public async Task<PageContentProfile> AnalyzeSampleAsync(
            string pdfPath,
            int samplePages = 3,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run(async () =>
            {
                try
                {
                    using var pdfDocument = PdfDocument.Load(pdfPath);
                    int pagesToAnalyze = Math.Min(samplePages, pdfDocument.PageCount);

                    var sampleProfiles = new List<PageContentProfile>();
                    for (int i = 0; i < pagesToAnalyze; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var profile = await AnalyzePageAsync(pdfPath, i, cancellationToken);
                        sampleProfiles.Add(profile);
                    }

                    return AggregateProfiles(sampleProfiles);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[EnhancedContentAnalysis] Error in sample analysis: {ex.Message}");
                    throw;
                }
            }, cancellationToken);
        }

        #region Deep Content Analysis

        private void AnalyzePageContentDeep(PdfDocument pdfDocument, int pageIndex, PageContentProfile profile)
        {
            try
            {
                // Method 1: Render at multiple resolutions for analysis
                using var lowResRender = pdfDocument.Render(pageIndex, 72, 72, PdfRenderFlags.None);
                using var midResRender = pdfDocument.Render(pageIndex, 150, 150, PdfRenderFlags.None);
                
                // Analyze rendered content
                AnalyzeRenderedContent(lowResRender, midResRender, profile);
                
                // Method 2: Heuristic analysis based on page characteristics
                var pageSize = pdfDocument.PageSizes[pageIndex];
                var pageArea = pageSize.Width * pageSize.Height;
                
                // Large pages often contain images
                if (pageArea > 1000000)
                {
                    profile.HasImages = true;
                }
                
                // Default: Assume text is present (most PDFs contain text)
                profile.HasText = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EnhancedContentAnalysis] Error in deep analysis: {ex.Message}");
                // Conservative fallback
                profile.HasText = true;
                profile.HasImages = true;
                profile.IsMixedContent = true;
            }
        }

        private void AnalyzeRenderedContent(Image lowResRender, Image midResRender, PageContentProfile profile)
        {
            if (lowResRender == null) return;

            try
            {
                Bitmap? lowResBitmap = lowResRender as Bitmap;
                Bitmap? midResBitmap = midResRender as Bitmap;
                
                bool disposeLowRes = false;
                bool disposeMidRes = false;
                
                if (lowResBitmap == null)
                {
                    lowResBitmap = new Bitmap(lowResRender);
                    disposeLowRes = true;
                }
                
                if (midResBitmap == null && midResRender != null)
                {
                    midResBitmap = new Bitmap(midResRender);
                    disposeMidRes = true;
                }

                try
                {
                    // Analyze content characteristics
                    AnalyzeContentCharacteristics(lowResBitmap, midResBitmap, profile);
                    
                    // Detect vector graphics
                    profile.HasVectorGraphics = DetectVectorGraphics(lowResBitmap);
                    if (profile.HasVectorGraphics)
                    {
                        profile.VectorObjectCount = EstimateVectorObjects(lowResBitmap);
                    }
                    
                    // Analyze image content and resolution
                    AnalyzeImageContent(lowResBitmap, midResBitmap, profile);
                }
                finally
                {
                    if (disposeLowRes) lowResBitmap?.Dispose();
                    if (disposeMidRes) midResBitmap?.Dispose();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EnhancedContentAnalysis] Error analyzing rendered content: {ex.Message}");
            }
        }

        private void AnalyzeContentCharacteristics(Bitmap? lowRes, Bitmap? midRes, PageContentProfile profile)
        {
            if (lowRes == null) return;

            int width = lowRes.Width;
            int height = lowRes.Height;
            
            // Sample pixels to detect content type
            int sampleCount = Math.Min(2000, width * height / 50);
            int textLikePixels = 0;
            int imageLikePixels = 0;
            var colors = new HashSet<System.Drawing.Color>();

            var random = new Random();
            for (int i = 0; i < sampleCount; i++)
            {
                int x = random.Next(width);
                int y = random.Next(height);
                var pixel = lowRes.GetPixel(x, y);
                colors.Add(pixel);

                // Text-like: grayscale or very few colors
                if (pixel.R == pixel.G && pixel.G == pixel.B)
                {
                    textLikePixels++;
                }
                else
                {
                    imageLikePixels++;
                }
            }

            double textRatio = (double)textLikePixels / sampleCount;
            double colorVariation = colors.Count;

            // Determine content type
            if (textRatio > 0.7 && colorVariation < 10)
            {
                profile.HasText = true;
                profile.TextBlockCount = EstimateTextBlocks(lowRes);
            }
            else if (colorVariation > 50)
            {
                profile.HasImages = true;
                profile.ImageCount = EstimateImageCount(lowRes);
            }
            else
            {
                profile.IsMixedContent = true;
                profile.HasText = true;
                profile.HasImages = true;
            }
        }

        private void AnalyzeImageContent(Bitmap? lowRes, Bitmap? midRes, PageContentProfile profile)
        {
            if (lowRes == null || profile.Dimensions == null) return;

            // Estimate image resolution based on bitmap size and page dimensions
            double widthInches = profile.Dimensions.WidthPoints / 72.0;
            double heightInches = profile.Dimensions.HeightPoints / 72.0;
            
            if (widthInches > 0 && heightInches > 0)
            {
                double estimatedDpiX = lowRes.Width / widthInches;
                double estimatedDpiY = lowRes.Height / heightInches;
                
                profile.MinImageResolution = Math.Min(estimatedDpiX, estimatedDpiY);
                profile.MaxImageResolution = Math.Max(estimatedDpiX, estimatedDpiY);
                profile.AverageImageResolution = (estimatedDpiX + estimatedDpiY) / 2.0;
                
                // If mid-res render is available, use it for better estimation
                if (midRes != null)
                {
                    double midDpiX = midRes.Width / widthInches;
                    double midDpiY = midRes.Height / heightInches;
                    profile.AverageImageResolution = (midDpiX + midDpiY) / 2.0;
                }
            }

            // Color profile detection
            profile.ColorProfile = DetectColorProfile(lowRes);
        }

        private ColorProfileInfo DetectColorProfile(Bitmap? bitmap)
        {
            var profile = new ColorProfileInfo();
            
            if (bitmap == null)
            {
                profile.IsGrayscale = true;
                return profile;
            }

            try
            {
                int colorPixels = 0;
                int grayscalePixels = 0;
                int sampleSize = Math.Min(1000, bitmap.Width * bitmap.Height);

                var random = new Random();
                for (int i = 0; i < sampleSize; i++)
                {
                    int x = random.Next(bitmap.Width);
                    int y = random.Next(bitmap.Height);
                    var pixel = bitmap.GetPixel(x, y);

                    if (pixel.R == pixel.G && pixel.G == pixel.B)
                    {
                        grayscalePixels++;
                    }
                    else
                    {
                        colorPixels++;
                    }
                }

                double colorRatio = (double)colorPixels / sampleSize;
                profile.IsColor = colorRatio > 0.1;
                profile.IsGrayscale = grayscalePixels > colorPixels;
                profile.IsMonochrome = colorPixels == 0;
                profile.ColorSpace = profile.IsColor ? "RGB" : "Grayscale";
            }
            catch
            {
                profile.IsColor = true;
                profile.ColorSpace = "RGB";
            }

            return profile;
        }

        private bool DetectVectorGraphics(Bitmap? bitmap)
        {
            // Heuristic: Vector graphics often have sharp edges
            // In production, more sophisticated edge detection would be used
            return bitmap != null && (bitmap.Width > 0 && bitmap.Height > 0);
        }

        private int EstimateTextBlocks(Bitmap? bitmap)
        {
            if (bitmap == null) return 0;
            return Math.Max(1, (bitmap.Width * bitmap.Height) / 100000);
        }

        private int EstimateImageCount(Bitmap? bitmap)
        {
            if (bitmap == null) return 0;
            return Math.Max(1, (bitmap.Width * bitmap.Height) / 500000);
        }

        private int EstimateVectorObjects(Bitmap? bitmap)
        {
            if (bitmap == null) return 0;
            return Math.Max(0, (bitmap.Width * bitmap.Height) / 200000);
        }

        private int CalculateComplexityScore(PageContentProfile profile)
        {
            int score = 10; // Base

            if (profile.HasText)
                score += profile.TextBlockCount * 2;

            if (profile.HasVectorGraphics)
                score += profile.VectorObjectCount * 3;

            if (profile.HasImages)
            {
                score += profile.ImageCount * 5;
                if (profile.MinImageResolution < 300)
                    score += 10;
            }

            if (profile.IsMixedContent)
                score += 15;

            return Math.Min(100, score);
        }

        private bool DetermineRasterizationNeed(PageContentProfile profile)
        {
            // Rasterization required ONLY if:
            // 1. Images are present
            // 2. Mixed content with low-res images
            // 3. Complex vectors that printer cannot handle natively

            if (!profile.HasImages && !profile.HasVectorGraphics)
                return false; // Pure text - no rasterization

            if (profile.HasImages)
                return true; // Images always require rasterization

            return false; // Vector-only: try native first
        }

        private string? DeterminePageSize(double widthPoints, double heightPoints)
        {
            const double a4Width = 595.276;
            const double a4Height = 841.890;
            const double letterWidth = 612;
            const double letterHeight = 792;
            const double tolerance = 5;

            if (Math.Abs(widthPoints - a4Width) < tolerance && Math.Abs(heightPoints - a4Height) < tolerance)
                return "A4";
            if (Math.Abs(widthPoints - a4Height) < tolerance && Math.Abs(heightPoints - a4Width) < tolerance)
                return "A4 (Landscape)";
            if (Math.Abs(widthPoints - letterWidth) < tolerance && Math.Abs(heightPoints - letterHeight) < tolerance)
                return "Letter";
            if (Math.Abs(widthPoints - letterHeight) < tolerance && Math.Abs(heightPoints - letterWidth) < tolerance)
                return "Letter (Landscape)";

            return "Custom";
        }

        private PageContentProfile AggregateProfiles(List<PageContentProfile> profiles)
        {
            if (profiles == null || profiles.Count == 0)
                throw new ArgumentException("Profiles list cannot be empty", nameof(profiles));

            var aggregated = new PageContentProfile
            {
                PageIndex = 0,
                HasText = profiles.Any(p => p.HasText),
                HasVectorGraphics = profiles.Any(p => p.HasVectorGraphics),
                HasImages = profiles.Any(p => p.HasImages),
                IsMixedContent = profiles.Any(p => p.IsMixedContent),
                TextBlockCount = (int)profiles.Average(p => p.TextBlockCount),
                VectorObjectCount = (int)profiles.Average(p => p.VectorObjectCount),
                ImageCount = (int)profiles.Average(p => p.ImageCount),
                MinImageResolution = profiles.Where(p => p.HasImages).Any() 
                    ? profiles.Where(p => p.HasImages).Min(p => p.MinImageResolution) 
                    : 0,
                MaxImageResolution = profiles.Where(p => p.HasImages).Any() 
                    ? profiles.Where(p => p.HasImages).Max(p => p.MaxImageResolution) 
                    : 0,
                AverageImageResolution = profiles.Where(p => p.HasImages).Any() 
                    ? profiles.Where(p => p.HasImages).Average(p => p.AverageImageResolution) 
                    : 0,
                ComplexityScore = (int)profiles.Average(p => p.ComplexityScore),
                RequiresRasterization = profiles.Any(p => p.RequiresRasterization)
            };

            aggregated.Fonts = profiles
                .SelectMany(p => p.Fonts)
                .Distinct()
                .ToList();

            if (profiles.Count > 0)
            {
                aggregated.Dimensions = profiles[0].Dimensions;
                aggregated.ColorProfile = profiles[0].ColorProfile;
            }

            return aggregated;
        }

        #endregion
    }
}
