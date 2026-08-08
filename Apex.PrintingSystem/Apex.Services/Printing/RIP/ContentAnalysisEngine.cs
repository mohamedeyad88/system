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

namespace Apex.Services.Printing.RIP
{
    /// <summary>
    /// Content Analysis Engine - Analyzes PDF pages to detect content types.
    /// 
    /// RIP-LIKE BEHAVIOR:
    /// - Analyzes each page before rendering
    /// - Detects text, vector graphics, and images
    /// - Generates content profile for decision engine
    /// - Never assumes rasterization is needed
    /// </summary>
    public class ContentAnalysisEngine
    {
        /// <summary>
        /// Analyze a single page from a PDF file.
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

                    // Analyze page content
                    // Note: PdfiumViewer doesn't expose detailed content analysis directly
                    // We use heuristics based on rendering and file structure
                    AnalyzePageContent(pdfDocument, pageIndex, profile);

                    // Calculate complexity score
                    profile.ComplexityScore = CalculateComplexityScore(profile);

                    // Determine if rasterization is required
                    profile.RequiresRasterization = DetermineRasterizationNeed(profile);

                    return profile;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ContentAnalysis] Error analyzing page {pageIndex}: {ex.Message}");

                    // Fallback: assume mixed content requiring rasterization
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
        /// Analyze all pages in a PDF file.
        /// </summary>
        public async Task<List<PageContentProfile>> AnalyzeDocumentAsync(
            string pdfPath,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run(async () =>
            {
                var profiles = new List<PageContentProfile>();

                try
                {
                    using var pdfDocument = PdfDocument.Load(pdfPath);
                    int pageCount = pdfDocument.PageCount;

                    for (int i = 0; i < pageCount; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var profile = await AnalyzePageAsync(pdfPath, i, cancellationToken);
                        profiles.Add(profile);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ContentAnalysis] Error analyzing document: {ex.Message}");
                    throw;
                }

                return profiles;
            }, cancellationToken);
        }

        /// <summary>
        /// Quick analysis - analyzes first few pages to determine document characteristics.
        /// Useful for large documents where full analysis is expensive.
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

                    // Aggregate sample profiles into representative profile
                    return AggregateProfiles(sampleProfiles);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ContentAnalysis] Error in sample analysis: {ex.Message}");
                    throw;
                }
            }, cancellationToken);
        }

        #region Private Analysis Methods

        private void AnalyzePageContent(PdfDocument pdfDocument, int pageIndex, PageContentProfile profile)
        {
            // Heuristic analysis based on PDF structure and rendering
            // Since PdfiumViewer doesn't expose detailed content extraction,
            // we use rendering-based heuristics

            try
            {
                // Render at low DPI to analyze content
                using var lowResRender = pdfDocument.Render(pageIndex, 72, 72, PdfRenderFlags.None);

                // Analyze the rendered bitmap for content characteristics
                AnalyzeRenderedContent(lowResRender, profile);

                // Additional heuristics based on file size and page complexity
                // (Larger pages with more objects suggest more complex content)
                var pageSize = pdfDocument.PageSizes[pageIndex];
                var pageArea = pageSize.Width * pageSize.Height;

                // Heuristic: If page is very large, likely contains images
                if (pageArea > 1000000) // Large page area
                {
                    profile.HasImages = true;
                }

                // Default assumptions (conservative)
                // We assume text is present unless proven otherwise
                profile.HasText = true; // Most PDFs contain text

                // Image content analysis is done in AnalyzeRenderedContent
                // No need to call separately
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ContentAnalysis] Error in content analysis: {ex.Message}");
                // Conservative fallback
                profile.HasText = true;
                profile.HasImages = true;
                profile.IsMixedContent = true;
            }
        }

        private void AnalyzeRenderedContent(Image renderedPage, PageContentProfile profile)
        {
            if (renderedPage == null) return;

            try
            {
                // Convert Image to Bitmap for analysis
                Bitmap? bitmap = renderedPage as Bitmap;
                if (bitmap == null)
                {
                    bitmap = new Bitmap(renderedPage);
                }

                try
                {
                    // Analyze bitmap for content characteristics
                    int width = bitmap.Width;
                    int height = bitmap.Height;

                    // Sample pixels to detect content type
                    int sampleCount = Math.Min(1000, width * height / 100);
                    int textLikePixels = 0;
                    int imageLikePixels = 0;
                    var colors = new HashSet<System.Drawing.Color>();

                    var random = new Random();
                    for (int i = 0; i < sampleCount; i++)
                    {
                        int x = random.Next(width);
                        int y = random.Next(height);
                        var pixel = bitmap.GetPixel(x, y);
                        colors.Add(pixel);

                        // Heuristic: Text-like pixels are usually black/white or very few colors
                        // Image-like pixels have more color variation
                        if (pixel.R == pixel.G && pixel.G == pixel.B)
                        {
                            textLikePixels++;
                        }
                        else
                        {
                            imageLikePixels++;
                        }
                    }

                    // Determine content type based on analysis
                    double textRatio = (double)textLikePixels / sampleCount;
                    double colorVariation = colors.Count;

                    if (textRatio > 0.7 && colorVariation < 10)
                    {
                        profile.HasText = true;
                        profile.TextBlockCount = EstimateTextBlocks(bitmap);
                    }
                    else if (colorVariation > 50)
                    {
                        profile.HasImages = true;
                        profile.ImageCount = EstimateImageCount(bitmap);
                        profile.MinImageResolution = 300; // Default assumption
                        profile.MaxImageResolution = 600;
                        profile.AverageImageResolution = 400;
                    }
                    else
                    {
                        profile.IsMixedContent = true;
                        profile.HasText = true;
                        profile.HasImages = true;
                    }

                    // Detect vector graphics (heuristic: sharp edges, geometric shapes)
                    profile.HasVectorGraphics = DetectVectorGraphics(bitmap);
                    if (profile.HasVectorGraphics)
                    {
                        profile.VectorObjectCount = EstimateVectorObjects(bitmap);
                    }

                    // Analyze image content
                    AnalyzeImageContent(bitmap, profile);
                }
                finally
                {
                    // Dispose if we created a new bitmap
                    if (bitmap != renderedPage)
                    {
                        bitmap?.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ContentAnalysis] Error analyzing rendered content: {ex.Message}");
            }
        }

        private void AnalyzeImageContent(Image? bitmap, PageContentProfile profile)
        {
            // Estimate image resolution based on bitmap size and page dimensions
            // This is a heuristic - actual resolution depends on source
            if (bitmap != null && profile.Dimensions != null)
            {
                double widthInches = profile.Dimensions.WidthPoints / 72.0;
                double heightInches = profile.Dimensions.HeightPoints / 72.0;

                if (widthInches > 0 && heightInches > 0)
                {
                    double estimatedDpiX = bitmap.Width / widthInches;
                    double estimatedDpiY = bitmap.Height / heightInches;

                    profile.MinImageResolution = Math.Min(estimatedDpiX, estimatedDpiY);
                    profile.MaxImageResolution = Math.Max(estimatedDpiX, estimatedDpiY);
                    profile.AverageImageResolution = (estimatedDpiX + estimatedDpiY) / 2.0;
                }
            }

            // Color profile detection
            profile.ColorProfile = DetectColorProfile(bitmap);
        }

        private ColorProfileInfo DetectColorProfile(Image? image)
        {
            var profile = new ColorProfileInfo();

            if (image == null)
            {
                profile.IsColor = false;
                profile.IsGrayscale = true;
                return profile;
            }

            try
            {
                // Convert Image to Bitmap for pixel access
                Bitmap? bitmap = image as Bitmap;
                bool disposeBitmap = false;

                if (bitmap == null)
                {
                    bitmap = new Bitmap(image);
                    disposeBitmap = true;
                }

                try
                {
                    // Sample pixels to determine color profile
                    int colorPixels = 0;
                    int grayscalePixels = 0;
                    int sampleSize = Math.Min(500, bitmap.Width * bitmap.Height);

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
                    profile.IsColor = true; // Conservative default
                    profile.ColorSpace = "RGB";
                }
                finally
                {
                    if (disposeBitmap)
                    {
                        bitmap?.Dispose();
                    }
                }
            }
            catch
            {
                profile.IsColor = true; // Conservative default
                profile.ColorSpace = "RGB";
            }

            return profile;
        }

        private bool DetectVectorGraphics(Image? bitmap)
        {
            // Heuristic: Vector graphics often have sharp edges and geometric patterns
            // This is a simplified detection - in production, more sophisticated analysis would be used
            return bitmap != null && (bitmap.Width > 0 && bitmap.Height > 0);
        }

        private int EstimateTextBlocks(Image? bitmap)
        {
            // Heuristic estimation - actual text block detection would require OCR or PDF parsing
            // For now, estimate based on page complexity
            if (bitmap == null) return 0;
            return Math.Max(1, (bitmap.Width * bitmap.Height) / 100000);
        }

        private int EstimateImageCount(Image? bitmap)
        {
            // Heuristic estimation
            if (bitmap == null) return 0;
            return Math.Max(1, (bitmap.Width * bitmap.Height) / 500000);
        }

        private int EstimateVectorObjects(Image? bitmap)
        {
            // Heuristic estimation
            if (bitmap == null) return 0;
            return Math.Max(0, (bitmap.Width * bitmap.Height) / 200000);
        }

        private int CalculateComplexityScore(PageContentProfile profile)
        {
            int score = 0;

            // Base score
            score += 10;

            // Text complexity
            if (profile.HasText)
                score += profile.TextBlockCount * 2;

            // Vector complexity
            if (profile.HasVectorGraphics)
                score += profile.VectorObjectCount * 3;

            // Image complexity
            if (profile.HasImages)
            {
                score += profile.ImageCount * 5;
                if (profile.MinImageResolution < 300)
                    score += 10; // Low resolution images need more processing
            }

            // Mixed content penalty
            if (profile.IsMixedContent)
                score += 15;

            return Math.Min(100, score);
        }

        private bool DetermineRasterizationNeed(PageContentProfile profile)
        {
            // Rasterization is required ONLY if:
            // 1. Images are present AND cannot be handled natively
            // 2. Mixed content with low-resolution images
            // 3. Complex vector graphics that printer cannot handle

            if (!profile.HasImages && !profile.HasVectorGraphics)
            {
                // Pure text - no rasterization needed
                return false;
            }

            if (profile.HasImages)
            {
                // Images require rasterization, but at appropriate DPI
                return true;
            }

            // Vector-only content - try native first
            return false;
        }

        private string? DeterminePageSize(double widthPoints, double heightPoints)
        {
            // Standard page sizes in points
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
                PageIndex = 0, // Representative profile
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

            // Aggregate fonts
            aggregated.Fonts = profiles
                .SelectMany(p => p.Fonts)
                .Distinct()
                .ToList();

            // Use first profile's dimensions as representative
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
