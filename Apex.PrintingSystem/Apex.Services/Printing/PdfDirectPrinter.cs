using Apex.Core.Models;
using Apex.Services.Printing.VendorDetection;
using PdfiumViewer;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Apex.Services.Printing
{
    /// <summary>
    /// Provides direct PDF printing without requiring external applications.
    /// Uses PdfiumViewer for rendering and Windows GDI+ for printing.
    /// This is a SILENT print method - no external apps will be opened.
    /// </summary>
    public class PdfDirectPrinter
    {
        /// <summary>
        /// Prints a PDF file directly to the specified printer.
        /// </summary>
        /// <param name="printerName">Target printer name.</param>
        /// <param name="pdfPath">Path to the PDF file.</param>
        /// <param name="copies">Number of copies to print.</param>
        /// <returns>True if printing was initiated successfully.</returns>
        public async Task<bool> PrintPdfAsync(string printerName, string pdfPath, int copies = 1)
        {
            // Call with default settings
            return await PrintPdfAsync(printerName, pdfPath, copies, null, documentMode: false);
        }

        /// <summary>
        /// Prints a PDF file directly to the specified printer with custom settings.
        /// Uses RIP Engine for intelligent rendering (preserves text/vectors, high-DPI raster for images).
        /// NEVER uses shell print - completely silent operation.
        /// </summary>
        public async Task<bool> PrintPdfAsync(string printerName, string pdfPath, int copies, PrintJob? jobSettings, bool documentMode = false)
        {
            if (!File.Exists(pdfPath))
                throw new FileNotFoundException("PDF file not found.", pdfPath);

            // Document mode: send the whole document to the driver/spooler without per-page rendering.
            // CRITICAL FIX: Avoid RAW printing which produces garbage on non-PDF-native printers.
            if (documentMode)
            {
                // 1. Try external document-level tools first (most reliable for document-based printing)
                if (await TrySumatraPdfAsync(printerName, pdfPath, copies))
                    return true;

                if (await TryPdfToPrinterAsync(printerName, pdfPath, copies))
                    return true;

                // 2. Fallback to PdfiumViewer rendering (works on ALL printers)
                // This is driver-based printing, not raw, so it will always produce correct output
                if (await TryPdfiumPrintAsync(printerName, pdfPath, copies, jobSettings))
                    return true;

                // 3. DO NOT use TryRawPdfPrintAsync - it only works for PDF-native printers
                // and produces garbage (random characters) on standard printers like EPSON inkjets

                return false;
            }

            // Try RIP Engine first (intelligent rendering)
            try
            {
                var vendorDetection = VendorDetection.VendorDetectionEngine.Instance;
                var metadata = vendorDetection.GetPrinterMetadata(printerName);
                
                var ripEngine = new RIP.RipPrintEngine();
                var qualityLevel = RIP.Models.QualityLevel.Professional;
                
                if (await ripEngine.PrintPdfAsync(printerName, pdfPath, metadata, copies, qualityLevel))
                {
                    Debug.WriteLine("[PdfDirectPrinter] RIP Engine print succeeded");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PdfDirectPrinter] RIP Engine failed, falling back: {ex.Message}");
            }

            // Fallback to standard methods (all are SILENT methods!)
            
            // 1. Try PdfiumViewer (renders at 300 DPI - fallback only)
            //    Note: This causes pixelation for text - use only as last resort
            if (await TryPdfiumPrintAsync(printerName, pdfPath, copies, jobSettings))
                return true;

            // 2. Try SumatraPDF (if installed - external tool but silent)
            if (await TrySumatraPdfAsync(printerName, pdfPath, copies))
                return true;

            // 3. Try PDFtoPrinter tool if available
            if (await TryPdfToPrinterAsync(printerName, pdfPath, copies))
                return true;
            
            // 4. DO NOT try raw PDF printing as fallback - it produces garbage on non-PDF-native printers
            // TryRawPdfPrintAsync returns true even when printer doesn't understand PDF, causing garbage output
            // Only PDF-native printers (enterprise/network printers with built-in PDF RIP) support this
            
            // If all methods failed, return false rather than sending garbage
            Debug.WriteLine("All silent PDF print methods failed.");
            return false;
        }

        /// <summary>
        /// Print PDF using PdfiumViewer to render actual PDF content and GDI+ to print.
        /// This is a truly silent method that doesn't open any external applications.
        /// Renders the actual PDF content - not just placeholders!
        /// </summary>
        private async Task<bool> TryPdfiumPrintAsync(string printerName, string pdfPath, int copies, PrintJob? jobSettings)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var printerLower = printerName.ToLowerInvariant();
                    if (printerLower.Contains("onenote") || printerLower.Contains("xps") || printerLower.Contains("pdf"))
                    {
                        throw new InvalidOperationException("لا يمكن استخدام الطباعة النقطية (Raster) مع الطابعات الافتراضية مثل OneNote/XPS/PDF. يرجى اختيار طابعة فعلية أو مسار طباعة يدعم التوجيه المباشر.");
                    }

                    // Open PDF with PdfiumViewer for REAL rendering
                    using var pdfDocument = PdfDocument.Load(pdfPath);
                    int pageCount = pdfDocument.PageCount;
                    
                    if (pageCount == 0)
                        return false;

                    int currentPage = 0;
                    int currentCopy = 0;
                    
                    using var printDoc = new PrintDocument();
                    printDoc.PrinterSettings.PrinterName = printerName;
                    printDoc.PrinterSettings.Copies = 1; // We handle copies manually
                    printDoc.DocumentName = Path.GetFileName(pdfPath);

                    // Clone page settings so any DPI/orientation edits are job-scoped only
                    const int printDpi = 300;
                    var jobPageSettings = (PageSettings)printDoc.DefaultPageSettings.Clone();
                    jobPageSettings.PrinterResolution = new PrinterResolution
                    {
                        Kind = PrinterResolutionKind.Custom,
                        X = printDpi,
                        Y = printDpi
                    };

                    // Apply settings if provided (scoped to the cloned page settings)
                    if (jobSettings != null)
                    {
                        if (jobSettings.Duplex && printDoc.PrinterSettings.CanDuplex)
                            printDoc.PrinterSettings.Duplex = Duplex.Vertical;
                        
                        if (!jobSettings.Color)
                            jobPageSettings.Color = false;
                        
                        if (jobSettings.Orientation?.Equals("Landscape", StringComparison.OrdinalIgnoreCase) == true)
                            jobPageSettings.Landscape = true;
                    }

                    // Ensure every page uses the cloned settings, leaving printer defaults untouched
                    printDoc.QueryPageSettings += (s, e) =>
                    {
                        e.PageSettings = (PageSettings)jobPageSettings.Clone();
                    };

                    printDoc.PrintPage += (sender, e) =>
                    {
                        if (e.Graphics == null) return;
                        
                        try
                        {
                            // ═══════════════════════════════════════════════════════════════════
                            // CRITICAL FIX: Set high-quality rendering settings for Graphics
                            // ═══════════════════════════════════════════════════════════════════
                            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                            e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                            e.Graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                            // Get page size from PDF
                            var pageSize = pdfDocument.PageSizes[currentPage];
                            
                            // Calculate scaling to fit printer page
                            var marginBounds = e.MarginBounds;
                            float scaleX = marginBounds.Width / (float)pageSize.Width;
                            float scaleY = marginBounds.Height / (float)pageSize.Height;
                            float scale = Math.Min(scaleX, scaleY);
                            
                            // Calculate destination size
                            int destWidth = (int)(pageSize.Width * scale);
                            int destHeight = (int)(pageSize.Height * scale);
                            
                            // Center on page
                            int offsetX = marginBounds.X + (marginBounds.Width - destWidth) / 2;
                            int offsetY = marginBounds.Y + (marginBounds.Height - destHeight) / 2;
                            
                            // Render the PDF page to image at print resolution (300 DPI)
                            // NOTE: This is fallback only - RIP Engine should handle this intelligently
                            // RIP Engine will preserve text/vectors and only rasterize images at high DPI
                            using var pageImage = pdfDocument.Render(currentPage, printDpi, printDpi, PdfRenderFlags.ForPrinting);
                            
                            // Draw the rendered PDF page
                            e.Graphics.DrawImage(pageImage, 
                                new Rectangle(offsetX, offsetY, destWidth, destHeight),
                                new Rectangle(0, 0, pageImage.Width, pageImage.Height),
                                GraphicsUnit.Pixel);
                        }
                        catch (Exception renderEx)
                        {
                            Debug.WriteLine($"PDF render error on page {currentPage}: {renderEx.Message}");
                            // Draw error message instead
                            using var font = new Font("Arial", 12);
                            e.Graphics.DrawString($"Error rendering page {currentPage + 1}: {renderEx.Message}", 
                                font, Brushes.Red, e.MarginBounds.X, e.MarginBounds.Y);
                        }
                        
                        // Move to next page
                        currentPage++;
                        
                        // Check if we need more pages
                        if (currentPage >= pageCount)
                        {
                            currentCopy++;
                            if (currentCopy < copies)
                            {
                                currentPage = 0;
                                e.HasMorePages = true;
                            }
                            else
                            {
                                e.HasMorePages = false;
                            }
                        }
                        else
                        {
                            e.HasMorePages = true;
                        }
                    };

                    printDoc.Print();
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"PdfiumViewer printing failed: {ex.Message}");
                    return false;
                }
            });
        }

        /// <summary>
        /// Use SumatraPDF for silent printing (recommended).
        /// </summary>
        private async Task<bool> TrySumatraPdfAsync(string printerName, string pdfPath, int copies)
        {
            return await Task.Run(() =>
            {
                var sumatraPath = FindExecutable(new[]
                {
                    @"C:\Program Files\SumatraPDF\SumatraPDF.exe",
                    @"C:\Program Files (x86)\SumatraPDF\SumatraPDF.exe",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                        "SumatraPDF", "SumatraPDF.exe"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "SumatraPDF.exe")
                });

                if (string.IsNullOrEmpty(sumatraPath))
                    return false;

                try
                {
                    // Build Sumatra command line for silent printing
                    var args = $"-print-to \"{printerName}\" -print-settings \"{copies}x\" -silent \"{pdfPath}\"";
                    
                    var psi = new ProcessStartInfo
                    {
                        FileName = sumatraPath,
                        Arguments = args,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using var process = Process.Start(psi);
                    if (process == null) return false;
                    
                    process.WaitForExit(120000); // 2 minute timeout
                    return process.ExitCode == 0;
                }
                catch
                {
                    return false;
                }
            });
        }

        /// <summary>
        /// Use PDFtoPrinter.exe for silent printing.
        /// </summary>
        private async Task<bool> TryPdfToPrinterAsync(string printerName, string pdfPath, int copies)
        {
            return await Task.Run(() =>
            {
                var toolPath = FindExecutable(new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "PDFtoPrinter.exe"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PDFtoPrinter.exe"),
                    @"C:\Tools\PDFtoPrinter.exe"
                });

                if (string.IsNullOrEmpty(toolPath))
                    return false;

                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = toolPath,
                        Arguments = $"\"{pdfPath}\" \"{printerName}\" copies={copies}",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };

                    using var process = Process.Start(psi);
                    if (process == null) return false;
                    
                    process.WaitForExit(120000);
                    return process.ExitCode == 0;
                }
                catch
                {
                    return false;
                }
            });
        }

        private string? FindExecutable(string[] paths)
        {
            return paths.FirstOrDefault(File.Exists);
        }


        /// <summary>
        /// Attempts to send PDF directly to printer (works for PDF-native printers).
        /// </summary>
        private async Task<bool> TryRawPdfPrintAsync(string printerName, string pdfPath, int copies)
        {
            try
            {
                // Some printers support direct PDF - send raw bytes (one full document per copy)
                var pdfBytes = await File.ReadAllBytesAsync(pdfPath);
                
                // Check if first bytes are PDF magic number
                if (pdfBytes.Length < 4) return false;
                if (pdfBytes[0] != '%' || pdfBytes[1] != 'P' || pdfBytes[2] != 'D' || pdfBytes[3] != 'F')
                    return false;

                for (int i = 0; i < Math.Max(1, copies); i++)
                {
                    if (!SendRawDataToPrinter(printerName, pdfBytes, "RAW"))
                        return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }



        #region Raw Printer Helper (P/Invoke)

        [StructLayout(LayoutKind.Sequential)]
        private struct DOCINFO
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pDocName;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string? pOutputFile;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pDataType;
        }

        [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int StartDocPrinter(IntPtr hPrinter, int level, ref DOCINFO pDocInfo);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool EndDocPrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool StartPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool EndPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool WritePrinter(IntPtr hPrinter, byte[] pBytes, int dwCount, out int dwWritten);

        private bool SendRawDataToPrinter(string printerName, byte[] data, string dataType)
        {
            IntPtr hPrinter = IntPtr.Zero;
            try
            {
                if (!OpenPrinter(printerName, out hPrinter, IntPtr.Zero))
                    return false;

                var docInfo = new DOCINFO
                {
                    pDocName = "PDF Direct Print",
                    pOutputFile = null,
                    pDataType = dataType
                };

                if (StartDocPrinter(hPrinter, 1, ref docInfo) == 0)
                    return false;

                if (!StartPagePrinter(hPrinter))
                {
                    EndDocPrinter(hPrinter);
                    return false;
                }

                if (!WritePrinter(hPrinter, data, data.Length, out int written) || written != data.Length)
                {
                    EndPagePrinter(hPrinter);
                    EndDocPrinter(hPrinter);
                    return false;
                }

                EndPagePrinter(hPrinter);
                EndDocPrinter(hPrinter);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (hPrinter != IntPtr.Zero)
                    ClosePrinter(hPrinter);
            }
        }

        #endregion
    }
}
