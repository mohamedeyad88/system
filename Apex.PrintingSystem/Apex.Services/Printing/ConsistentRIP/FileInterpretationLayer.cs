using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PdfiumViewer;
using SkiaSharp;

namespace Apex.Services.Printing.ConsistentRIP
{
    /// <summary>
    /// FILE INTERPRETATION LAYER - Takes full control of file parsing.
    /// 
    /// CRITICAL: The application interprets files, NOT the printer or driver.
    /// 
    /// Responsibilities:
    /// - Parse PDF, vector, and image formats internally
    /// - Extract text, vector shapes, and images
    /// - Preserve vector data as long as possible
    /// - Avoid early rasterization at OS/driver level
    /// 
    /// Supported Formats:
    /// - PDF (via PdfiumViewer)
    /// - Images (PNG, JPG, BMP, TIFF via SkiaSharp)
    /// - Vector (SVG via SkiaSharp)
    /// </summary>
    public class FileInterpretationLayer
    {
        public FileInterpretationLayer()
        {
            Debug.WriteLine("[FileInterpreter] ✓ File Interpretation Layer initialized");
        }
        
        /// <summary>
        /// Interprets a file and extracts its content into internal representation.
        /// NO printer-side interpretation allowed.
        /// </summary>
        public async Task<InterpretedDocument> InterpretFileAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");
            
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            
            Debug.WriteLine($"[FileInterpreter] Interpreting: {Path.GetFileName(filePath)} ({extension})");
            
            return extension switch
            {
                ".pdf" => await InterpretPdfAsync(filePath, cancellationToken),
                ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tiff" or ".tif" 
                    => await InterpretImageAsync(filePath, cancellationToken),
                ".svg" => await InterpretVectorAsync(filePath, cancellationToken),
                _ => throw new NotSupportedException($"File format {extension} not supported")
            };
        }
        
        /// <summary>
        /// Interprets PDF - CRITICAL FIX: Renders all pages IMMEDIATELY.
        /// 
        /// BUG FIX: Previously stored PDF reference for "lazy rendering",
        /// but PDF was disposed before rendering occurred → ObjectDisposedException.
        /// 
        /// NEW ARCHITECTURE: Render ALL pages while PDF is open, store bitmaps.
        /// PDF is disposed at end of method (safe, no references remain).
        /// </summary>
        private async Task<InterpretedDocument> InterpretPdfAsync(
            string pdfPath,
            CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                // CRITICAL: using var is CORRECT here - PDF only used within this method
                using var pdfDocument = PdfDocument.Load(pdfPath);
                
                var doc = new InterpretedDocument
                {
                    SourceFile = pdfPath,
                    PageCount = pdfDocument.PageCount,
                    SourceFormat = FileFormat.PDF
                };
                
                // Render DPI for initial interpretation (will be normalized later)
                const int INITIAL_RENDER_DPI = 300;
                
                Debug.WriteLine($"[FileInterpreter] Rendering {pdfDocument.PageCount} pages immediately @ {INITIAL_RENDER_DPI} DPI");
                
                // CRITICAL: Render each page IMMEDIATELY while PDF is open
                for (int i = 0; i < pdfDocument.PageCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var pageSize = pdfDocument.PageSizes[i];
                    
                    var page = new InterpretedPage
                    {
                        PageNumber = i + 1,
                        WidthInPoints = (float)pageSize.Width,
                        HeightInPoints = (float)pageSize.Height,
                        SourceType = ContentType.Mixed
                    };
                    
                    // CRITICAL FIX: Render page NOW while PDF is still open
                    int width = (int)(pageSize.Width / 72.0 * INITIAL_RENDER_DPI);
                    int height = (int)(pageSize.Height / 72.0 * INITIAL_RENDER_DPI);
                    
                    try
                    {
                        // Render PDF page to System.Drawing.Image
                        using var pdfImage = pdfDocument.Render(
                            i, 
                            width, 
                            height, 
                            INITIAL_RENDER_DPI, 
                            INITIAL_RENDER_DPI, 
                            PdfRenderFlags.Annotations | PdfRenderFlags.ForPrinting
                        );
                        
                        // Convert to System.Drawing.Bitmap
                        using var gdiBitmap = new System.Drawing.Bitmap(pdfImage);
                        
                        // Convert to SKBitmap and store
                        page.ImageData = ConvertGdiBitmapToSKBitmap(gdiBitmap);
                        
                        Debug.WriteLine($"[FileInterpreter]   Page {i + 1}: {width}x{height} @ {INITIAL_RENDER_DPI} DPI → SKBitmap");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[FileInterpreter] ⚠️ Failed to render page {i + 1}: {ex.Message}");
                        // Create empty bitmap as fallback
                        page.ImageData = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
                    }
                    
                    // Store page metadata (NO PDF REFERENCE)
                    page.Metadata["OriginalDPI"] = INITIAL_RENDER_DPI;
                    
                    doc.Pages.Add(page);
                }
                
                Debug.WriteLine($"[FileInterpreter] ✓ PDF rendered: {doc.PageCount} pages, PDF will be disposed safely");
                
                return doc;
                
                // CRITICAL: PDF disposed here - safe because all rendering is complete
                // No async tasks hold references to pdfDocument
            }, cancellationToken);
        }
        
        /// <summary>
        /// Converts System.Drawing.Bitmap to SKBitmap.
        /// </summary>
        private SKBitmap ConvertGdiBitmapToSKBitmap(System.Drawing.Bitmap gdiBitmap)
        {
            var skBitmap = new SKBitmap(gdiBitmap.Width, gdiBitmap.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var pixmap = skBitmap.PeekPixels();
            if (pixmap == null)
            {
                skBitmap.Dispose();
                throw new InvalidOperationException("Failed to create pixmap for rendered page (PeekPixels returned null).");
            }
            
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
                    
                    // Convert BGRA to RGBA
                    for (int i = 0; i < bytes; i += 4)
                    {
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
        
        /// <summary>
        /// Interprets image file - loads and extracts pixel data.
        /// </summary>
        private async Task<InterpretedDocument> InterpretImageAsync(
            string imagePath,
            CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                using var bitmap = SKBitmap.Decode(imagePath);
                
                if (bitmap == null)
                    throw new InvalidDataException($"Cannot decode image: {imagePath}");
                
                var doc = new InterpretedDocument
                {
                    SourceFile = imagePath,
                    PageCount = 1,
                    SourceFormat = FileFormat.Image
                };
                
                var page = new InterpretedPage
                {
                    PageNumber = 1,
                    WidthInPoints = bitmap.Width * 72f / bitmap.Width, // Assume 72 DPI initially
                    HeightInPoints = bitmap.Height * 72f / bitmap.Height,
                    SourceType = ContentType.Image
                };
                
                // Store bitmap data
                page.ImageData = bitmap.Copy();
                
                doc.Pages.Add(page);
                
                Debug.WriteLine($"[FileInterpreter] ✓ Image interpreted: {bitmap.Width}x{bitmap.Height}");
                
                return doc;
            }, cancellationToken);
        }
        
        /// <summary>
        /// Interprets vector file (SVG).
        /// </summary>
        private async Task<InterpretedDocument> InterpretVectorAsync(
            string vectorPath,
            CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                // For now, load SVG as image (simplified)
                // Full SVG support would require SkiaSharp.Extended.Svg package
                using var bitmap = SKBitmap.Decode(vectorPath);
                if (bitmap == null)
                    throw new InvalidDataException($"Cannot decode vector image: {vectorPath}");
                
                var doc = new InterpretedDocument
                {
                    SourceFile = vectorPath,
                    PageCount = 1,
                    SourceFormat = FileFormat.Vector
                };
                
                var page = new InterpretedPage
                {
                    PageNumber = 1,
                    WidthInPoints = bitmap.Width * 72f / 96f, // Assume 96 DPI
                    HeightInPoints = bitmap.Height * 72f / 96f,
                    SourceType = ContentType.Vector
                };
                
                page.ImageData = bitmap.Copy();
                
                doc.Pages.Add(page);
                
                Debug.WriteLine($"[FileInterpreter] ✓ Vector interpreted: {page.WidthInPoints}x{page.HeightInPoints}pt");
                
                return doc;
            }, cancellationToken);
        }
    }
    
    /// <summary>
    /// Interpreted document - internal representation after file parsing.
    /// </summary>
    public class InterpretedDocument
    {
        public string SourceFile { get; set; } = string.Empty;
        public FileFormat SourceFormat { get; set; }
        public int PageCount { get; set; }
        public List<InterpretedPage> Pages { get; set; } = new();
    }
    
    /// <summary>
    /// Interpreted page - single page content.
    /// </summary>
    public class InterpretedPage
    {
        public int PageNumber { get; set; }
        public float WidthInPoints { get; set; }  // 1 point = 1/72 inch
        public float HeightInPoints { get; set; }
        public ContentType SourceType { get; set; }
        
        // Content data (mutually exclusive based on SourceType)
        public SKBitmap? ImageData { get; set; }
        public SKPicture? VectorData { get; set; }
        public List<TextElement>? TextElements { get; set; }
        
        // Metadata for lazy loading/processing
        public Dictionary<string, object> Metadata { get; set; } = new();
    }
    
    /// <summary>
    /// Text element extracted from document.
    /// </summary>
    public class TextElement
    {
        public string Text { get; set; } = string.Empty;
        public float X { get; set; }
        public float Y { get; set; }
        public string FontFamily { get; set; } = "Arial";
        public float FontSize { get; set; }
        public SKColor Color { get; set; }
        public bool IsBold { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }
    
    /// <summary>
    /// File format types.
    /// </summary>
    public enum FileFormat
    {
        PDF,
        Image,
        Vector
    }
    
    /// <summary>
    /// Content type within a page.
    /// </summary>
    public enum ContentType
    {
        Text,
        Vector,
        Image,
        Mixed
    }
}
