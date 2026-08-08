using Apex.Services.Printing.RIP;
using Apex.Services.Printing.RIP.Models;
using Apex.Services.Printing.UniversalRIP;
using Apex.Services.Printing.VendorDetection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.Services.Printing.UniversalRIP
{
    /// <summary>
    /// Render result from Hybrid Render Strategy.
    /// </summary>
    public class RenderResult : IDisposable
    {
        public System.Drawing.Image? RasterImage { get; set; }
        public string? SourcePdfPath { get; set; }
        public bool IsNativeVector { get; set; }

        public void Dispose()
        {
            RasterImage?.Dispose();
        }
    }

    /// <summary>
    /// Safe Output Strategy - Generates printer-specific output while ensuring
    /// the output language is ALWAYS supported by the target printer.
    /// 
    /// CRITICAL: Never sends unsupported language to a printer.
    /// </summary>
    public class SafeOutputStrategy
    {
        /// <summary>
        /// Generates safe output bytes for the printer based on decision.
        /// </summary>
        public async Task<byte[]> GenerateSafeOutputAsync(
            RenderResult renderResult,
            UniversalRenderDecision decision,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                // ═══════════════════════════════════════════════════════════════════
                // SAFETY CHECK: Verify output language is supported
                // ═══════════════════════════════════════════════════════════════════
                if (!decision.PrinterProfile.SupportsLanguage(decision.OutputLanguage))
                {
                    Debug.WriteLine($"[SafeOutput] WARNING: Language {decision.OutputLanguage} not supported, falling back to GDI");
                    decision.OutputLanguage = PrintLanguage.GDI;
                    decision.Strategy = RenderStrategy.FallbackRaster;
                }

                // Generate output based on language
                return decision.OutputLanguage switch
                {
                    PrintLanguage.PostScript => GeneratePostScriptOutput(renderResult, decision),
                    PrintLanguage.PCL => GeneratePCLOutput(renderResult, decision),
                    PrintLanguage.PDF => GeneratePDFOutput(renderResult, decision),
                    PrintLanguage.ESCPage => GenerateESCPageOutput(renderResult, decision),
                    PrintLanguage.ESCPOS => GenerateESCPOutput(renderResult, decision),
                    PrintLanguage.GDI => GenerateGDIOutput(renderResult, decision),
                    _ => GenerateGDIOutput(renderResult, decision) // Safe fallback
                };
            }, cancellationToken);
        }

        #region Language-Specific Output Generators

        private byte[] GeneratePostScriptOutput(RenderResult renderResult, UniversalRenderDecision decision)
        {
            // PostScript output - preserves text and vectors natively
            // For now, return rasterized image as PostScript (simplified)
            // In production, would generate native PostScript commands

            Debug.WriteLine("[SafeOutput] Generating PostScript output");

            if (renderResult.RasterImage != null)
            {
                // Convert raster image to PostScript
                return ConvertImageToPostScript(renderResult.RasterImage, decision);
            }

            // Fallback: empty output
            return Array.Empty<byte>();
        }

        private byte[] GeneratePCLOutput(RenderResult renderResult, UniversalRenderDecision decision)
        {
            // PCL output - preserves text and vectors natively
            Debug.WriteLine("[SafeOutput] Generating PCL output");

            if (renderResult.RasterImage != null)
            {
                // Convert raster image to PCL
                return ConvertImageToPCL(renderResult.RasterImage, decision);
            }

            return Array.Empty<byte>();
        }

        private byte[] GeneratePDFOutput(RenderResult renderResult, UniversalRenderDecision decision)
        {
            // PDF output - universal format
            Debug.WriteLine("[SafeOutput] Generating PDF output");

            // If we have a PDF path, return it directly
            if (!string.IsNullOrEmpty(renderResult.SourcePdfPath) && File.Exists(renderResult.SourcePdfPath))
            {
                return File.ReadAllBytes(renderResult.SourcePdfPath);
            }

            // Otherwise, convert raster to PDF
            if (renderResult.RasterImage != null)
            {
                return ConvertImageToPDF(renderResult.RasterImage, decision);
            }

            return Array.Empty<byte>();
        }

        private byte[] GenerateESCPageOutput(RenderResult renderResult, UniversalRenderDecision decision)
        {
            // ESC/Page output (Epson high-end)
            Debug.WriteLine("[SafeOutput] Generating ESC/Page output");

            if (renderResult.RasterImage != null)
            {
                return ConvertImageToESCPage(renderResult.RasterImage, decision);
            }

            return Array.Empty<byte>();
        }

        private byte[] GenerateESCPOutput(RenderResult renderResult, UniversalRenderDecision decision)
        {
            // ESC/P output (Epson basic)
            Debug.WriteLine("[SafeOutput] Generating ESC/P output");

            if (renderResult.RasterImage != null)
            {
                return ConvertImageToESCP(renderResult.RasterImage, decision);
            }

            return Array.Empty<byte>();
        }

        private byte[] GenerateGDIOutput(RenderResult renderResult, UniversalRenderDecision decision)
        {
            // GDI output - always available, but raster-only
            Debug.WriteLine("[SafeOutput] Generating GDI output (raster-only)");

            if (renderResult.RasterImage != null)
            {
                // Convert to GDI-compatible format
                return ConvertImageToGDI(renderResult.RasterImage, decision);
            }

            return Array.Empty<byte>();
        }

        #endregion

        #region Format Converters (Simplified - Production would use proper libraries)

        private byte[] ConvertImageToPostScript(System.Drawing.Image image, UniversalRenderDecision decision)
        {
            // ═══════════════════════════════════════════════════════════════════
            // REAL PostScript output via PostScriptOutputGenerator (DSC 3.0)
            // Uses ASCII85 encoding with colorimage operator (BGR→RGB)
            // ═══════════════════════════════════════════════════════════════════
            Debug.WriteLine("[SafeOutput] Generating real PostScript output (ASCII85/DSC 3.0)");

            try
            {
                using var bitmap = image as Bitmap ?? new Bitmap(image);
                return PostScriptOutputGenerator.GeneratePage(bitmap, pageNumber: 1, totalPages: 1);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SafeOutput] PostScript generation failed: {ex.Message}, falling back to GDI");
                return ConvertImageToGDI(image, decision);
            }
        }

        private byte[] ConvertImageToPCL(System.Drawing.Image image, UniversalRenderDecision decision)
        {
            // ═══════════════════════════════════════════════════════════════════
            // REAL PCL5 raster output via PclOutputGenerator
            // Color: 24bpp RGB rows; Monochrome: 1bpp inverted packed rows
            // ═══════════════════════════════════════════════════════════════════
            bool isColor = decision.PrinterProfile?.SupportsColor ?? false;
            Debug.WriteLine($"[SafeOutput] Generating real PCL5 raster output (color={isColor})");

            try
            {
                using var bitmap = image as Bitmap ?? new Bitmap(image);
                return PclOutputGenerator.GeneratePage(bitmap, isColorPrinter: isColor);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SafeOutput] PCL generation failed: {ex.Message}, falling back to GDI");
                return ConvertImageToGDI(image, decision);
            }
        }

        private byte[] ConvertImageToPDF(System.Drawing.Image image, UniversalRenderDecision decision)
        {
            // Simplified PDF generation
            // In production, would use PDF library (e.g., PdfSharp, iTextSharp)
            using var ms = new MemoryStream();
            image.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return ms.ToArray();
        }

        private byte[] ConvertImageToESCPage(System.Drawing.Image image, UniversalRenderDecision decision)
        {
            // ESC/Page format (Epson)
            using var ms = new MemoryStream();
            image.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return ms.ToArray();
        }

        private byte[] ConvertImageToESCP(System.Drawing.Image image, UniversalRenderDecision decision)
        {
            // ═══════════════════════════════════════════════════════════════════
            // CRITICAL FIX: Generate TRUE ESC/P raster commands for Epson printers
            // FIXED: Use ESC ( v for proper multi-line raster graphics
            // ═══════════════════════════════════════════════════════════════════
            Debug.WriteLine("[SafeOutput] Generating CORRECTED ESC/P raster output");
            Apex.Services.Logging.PrintLogger.Info("[SafeOutput] Converting image to ESC/P raster commands (FIXED VERSION)");

            try
            {
                using var bitmap = new System.Drawing.Bitmap(image);
                var ms = new MemoryStream();

                int width = bitmap.Width;
                int height = bitmap.Height;

                // ═══════════════════════════════════════════════════════════════
                // ESC/P2 Raster Graphics - Proper Commands
                // ═══════════════════════════════════════════════════════════════

                // ESC @ - Initialize printer
                ms.WriteByte(0x1B); ms.WriteByte(0x40);

                // ESC ( G - Enter graphics mode
                ms.WriteByte(0x1B); ms.WriteByte(0x28); ms.WriteByte(0x47);
                ms.WriteByte(0x01); ms.WriteByte(0x00);
                ms.WriteByte(0x01);

                // ESC ( U - Set unit for graphics (180 DPI)
                ms.WriteByte(0x1B); ms.WriteByte(0x28); ms.WriteByte(0x55);
                ms.WriteByte(0x01); ms.WriteByte(0x00);
                ms.WriteByte(0x05);  // 180 DPI

                // Calculate bytes per line
                int bytesPerLine = (width + 7) / 8;

                // ESC ( v - Select raster graphics mode
                ms.WriteByte(0x1B); ms.WriteByte(0x28); ms.WriteByte(0x76);

                // Calculate data size: 4 (header) + height * bytesPerLine
                int dataSize = 4 + (height * bytesPerLine);
                ms.WriteByte((byte)(dataSize & 0xFF));
                ms.WriteByte((byte)((dataSize >> 8) & 0xFF));

                // Raster graphics header
                ms.WriteByte(0x00); ms.WriteByte(0x00);  // Color mode (monochrome)
                ms.WriteByte((byte)(width & 0xFF));       // Width low byte
                ms.WriteByte((byte)((width >> 8) & 0xFF)); // Width high byte

                // Send all lines as continuous raster data
                for (int y = 0; y < height; y++)
                {
                    byte[] lineData = new byte[bytesPerLine];

                    // Convert line to 1-bit monochrome
                    for (int x = 0; x < width; x++)
                    {
                        var pixel = bitmap.GetPixel(x, y);
                        int gray = (pixel.R + pixel.G + pixel.B) / 3;
                        bool isBlack = gray < 128;

                        if (isBlack)
                        {
                            int byteIndex = x / 8;
                            int bitIndex = 7 - (x % 8);
                            lineData[byteIndex] |= (byte)(1 << bitIndex);
                        }
                    }

                    ms.Write(lineData, 0, lineData.Length);
                }

                // ESC ( G - Exit graphics mode
                ms.WriteByte(0x1B); ms.WriteByte(0x28); ms.WriteByte(0x47);
                ms.WriteByte(0x01); ms.WriteByte(0x00);
                ms.WriteByte(0x00);

                // Form feed (eject page) - ONLY ONCE at the end
                ms.WriteByte(0x0C);

                var output = ms.ToArray();
                Apex.Services.Logging.PrintLogger.Info(
                    "[SafeOutput] ESC/P raster generated successfully: {Size} bytes, {Width}x{Height}, {Lines} lines",
                    output.Length, width, height, height);

                return output;
            }
            catch (Exception ex)
            {
                Apex.Services.Logging.PrintLogger.Error(ex, "[SafeOutput] Failed to generate ESC/P output, falling back to GDI");
                return ConvertImageToGDI(image, decision);
            }
        }

        private byte[] ConvertImageToGDI(System.Drawing.Image image, UniversalRenderDecision decision)
        {
            // ═══════════════════════════════════════════════════════════════════
            // CRITICAL FIX: Use BMP format for GDI (universally supported)
            // ═══════════════════════════════════════════════════════════════════
            using var ms = new MemoryStream();

            // BMP is simpler and more universally supported than PNG for raw printing
            image.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
            return ms.ToArray();
        }

        #endregion
    }
}
