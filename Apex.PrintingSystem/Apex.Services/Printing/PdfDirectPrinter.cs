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

            // Print-to-file virtual printers (Microsoft Print to PDF / XPS) need the
            // PrintToFile + PrintFileName route implemented in TryPdfiumPrintAsync.
            // The RIP engine and raw paths "succeed" against them while the spooler
            // silently drops the job (QA-measured: success reported, no output file).
            var plower = printerName.ToLowerInvariant();
            if (plower.Contains("microsoft print to pdf") || plower.Contains("xps"))
            {
                Debug.WriteLine("[PdfDirectPrinter] Virtual print-to-file printer → Pdfium PrintToFile route");
                return await TryPdfiumPrintAsync(printerName, pdfPath, copies, jobSettings);
            }

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

            // 1. The printer's own Windows driver, via Pdfium + GDI+.
            //
            // This runs first because it is the only path that carries the operator's
            // choices — duplex, colour, paper, quality — to the device, and the only
            // one that submits the document as a single spooler job.
            //
            // The RIP engine below used to run first. It rasterises page by page and
            // sends each page as its own RAW job, so a 106-page book became 106 queue
            // entries, and one bad page stopped the rest. It stays as a fallback for
            // devices the driver path cannot drive.
            if (await TryPdfiumPrintAsync(printerName, pdfPath, copies, jobSettings))
                return true;

            Debug.WriteLine("[PdfDirectPrinter] Driver path did not print — trying the RIP engine");

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
                Apex.Services.Logging.PrintLogger.Error(ex,
                    "[PdfDirectPrinter] RIP fallback failed for '{Printer}'", printerName);
            }

            // 3. External silent printers, last: neither honours jobSettings, so a job
            //    printed this way loses duplex/colour/quality.
            if (await TrySumatraPdfAsync(printerName, pdfPath, copies))
                return true;

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
                    // Print-to-file virtual printers (Microsoft Print to PDF / XPS):
                    // without an explicit output file name the spooler silently
                    // drops the job (measured in QA: "success" reported, no file,
                    // no queue entry). Instead of rejecting them, route through
                    // PrintToFile with an auto-derived name next to the source.
                    var printerLower = printerName.ToLowerInvariant();
                    string? autoOutputFile = null;
                    if (printerLower.Contains("microsoft print to pdf"))
                    {
                        autoOutputFile = Path.Combine(
                            Path.GetDirectoryName(pdfPath) ?? Path.GetTempPath(),
                            $"{Path.GetFileNameWithoutExtension(pdfPath)}-printed-{DateTime.Now:yyyyMMdd-HHmmss}.pdf");
                    }
                    else if (printerLower.Contains("xps"))
                    {
                        autoOutputFile = Path.Combine(
                            Path.GetDirectoryName(pdfPath) ?? Path.GetTempPath(),
                            $"{Path.GetFileNameWithoutExtension(pdfPath)}-printed-{DateTime.Now:yyyyMMdd-HHmmss}.oxps");
                    }
                    else if (printerLower.Contains("onenote"))
                    {
                        // OneNote's driver needs its own UI session; raster output
                        // would vanish. Fail fast with a clear reason instead of
                        // reporting false success.
                        throw new InvalidOperationException(
                            "طابعة OneNote الافتراضية غير مدعومة للطباعة الصامتة — اختر طابعة فعلية أو Microsoft Print to PDF.");
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

                    // Silent output for print-to-file virtual printers.
                    if (autoOutputFile != null)
                    {
                        printDoc.PrinterSettings.PrintToFile = true;
                        printDoc.PrinterSettings.PrintFileName = autoOutputFile;
                        Debug.WriteLine($"PdfDirectPrinter: virtual printer → PrintToFile: {autoOutputFile}");
                    }

                    // Clone page settings so any DPI/orientation edits are job-scoped only
                    var jobPageSettings = (PageSettings)printDoc.DefaultPageSettings.Clone();

                    // Print at the DEVICE's own resolution, not a hardcoded 300.
                    //
                    // This used to force PrinterResolution to Custom 300×300 and then
                    // rasterise every page to a 300 DPI bitmap. Both are downgrades: a
                    // business inkjet or laser renders at 600–1200 DPI, and text in a
                    // PDF is vector — handing the driver a 300 DPI picture of that text
                    // throws the device's whole resolution advantage away. The result is
                    // visibly softer than printing the same PDF from a PDF reader.
                            int printDpi = ResolveDeviceDpi(
                                printDoc.PrinterSettings, jobPageSettings, jobSettings?.Quality);

                    // The values a real device actually got. On a virtual printer none
                    // of this is exercised, so a field test on real hardware is the
                    // first time these are proven — and if a sheet comes out wrong,
                    // this line says whether the fault was in what we asked for.
                    Apex.Services.Logging.PrintLogger.Info(
                        "[PdfDirectPrinter] {Printer} | dpi={Dpi} | duplex={Duplex} | colour={Colour} | " +
                        "orientation={Orientation} | paper={Paper} | copies={Copies}",
                        printerName,
                        printDpi,
                        jobSettings?.Duplex,
                        jobSettings?.Color,
                        jobSettings?.Orientation,
                        jobPageSettings.PaperSize?.PaperName,
                        copies);

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

                            // Get page size from PDF (points, 1/72 inch)
                            var pageSize = pdfDocument.PageSizes[currentPage];

                            // Print at ACTUAL SIZE.
                            //
                            // The old code divided MarginBounds (hundredths of an inch)
                            // by the page size (points) and used the result as a scale.
                            // Those are different units, so an A4 page came out at about
                            // 76% — and it also fitted to the one-inch default margins,
                            // shrinking it further. On a numbered book or an imposed
                            // sheet that is not just "smaller": every number and every
                            // crop mark lands in the wrong place once the stack is cut.
                            const double PointToHundredthsInch = 100.0 / 72.0;
                            double naturalW = pageSize.Width * PointToHundredthsInch;
                            double naturalH = pageSize.Height * PointToHundredthsInch;

                            // Only shrink when the sheet genuinely cannot hold the page.
                            var printable = e.PageSettings.PrintableArea;
                            double availW = printable.Width > 0 ? printable.Width : e.PageBounds.Width;
                            double availH = printable.Height > 0 ? printable.Height : e.PageBounds.Height;

                            double scale = 1.0;
                            if (naturalW > availW || naturalH > availH)
                                scale = Math.Min(availW / naturalW, availH / naturalH);

                            int destWidth = (int)Math.Round(naturalW * scale);
                            int destHeight = (int)Math.Round(naturalH * scale);

                            // Origin is the printable area's corner (OriginAtMargins is false).
                            int offsetX = (int)Math.Round((availW - destWidth) / 2.0);
                            int offsetY = (int)Math.Round((availH - destHeight) / 2.0);

                            // Rasterise at exactly the size the page will occupy on the
                            // device. Rendering at one resolution and letting GDI+ rescale
                            // into another resamples the whole page a second time — a
                            // second blur on top of the one rasterising already cost us.
                            // destWidth/destHeight are hundredths of an inch.
                            int pxWidth = Math.Max(1, (int)Math.Round(destWidth / 100.0 * printDpi));
                            int pxHeight = Math.Max(1, (int)Math.Round(destHeight / 100.0 * printDpi));

                            using var pageImage = pdfDocument.Render(
                                currentPage, pxWidth, pxHeight, printDpi, printDpi, PdfRenderFlags.ForPrinting);

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

        /// <summary>Below this the output looks visibly soft; no modern device is slower.</summary>
        private const int MinimumUsableDpi = 300;

        /// <summary>
        /// Rasterising above this buys nothing the eye can see on paper while the bitmap
        /// grows with the square of the resolution — an A3 page at 1200 DPI is roughly
        /// 200 MB, enough to stall the spooler.
        /// </summary>
        private const int MaximumPracticalDpi = 600;

        /// <summary>
        /// The resolution this device actually prints at.
        ///
        /// The page was previously pinned to 300 DPI regardless of the printer. Asking
        /// the driver what it supports is the difference between using a 600 or 1200 DPI
        /// device properly and throwing half its resolution away on every job.
        /// </summary>
        /// <param name="requestedQuality">
        /// The operator's choice from the print screen — "Draft", "Normal", "High" or
        /// "Best". It used to be collected and then ignored here, so every job
        /// rasterised at the device maximum. That costs real time: a page at 600 DPI
        /// carries four times the pixels of the same page at 300, and a book is
        /// hundreds of pages across several printers. Draft and Normal cap the raster
        /// below the device maximum; High and Best let the device have its full
        /// resolution.
        /// </param>
        private static int ResolveDeviceDpi(
            PrinterSettings settings, PageSettings pageSettings, string? requestedQuality = null)
        {
            int best = 0;

            try
            {
                foreach (PrinterResolution r in settings.PrinterResolutions)
                {
                    // Named kinds (Draft/Low/Medium/High) report X/Y as negative enums.
                    if (r.X > 0 && r.Y > 0)
                        best = Math.Max(best, Math.Min(r.X, r.Y));
                }

                // The driver's own current choice, when it reports one.
                var current = pageSettings.PrinterResolution;
                if (current != null && current.X > 0 && current.Y > 0)
                    best = Math.Max(best, Math.Min(current.X, current.Y));
            }
            catch (Exception ex)
            {
                // A driver that will not answer is not a reason to fail the job.
                Debug.WriteLine($"PdfDirectPrinter: could not read printer resolutions — {ex.Message}");
            }

            if (best <= 0) best = MinimumUsableDpi;

            best = Math.Clamp(best, MinimumUsableDpi, MaximumPracticalDpi);

            // Never raise the device's own resolution — only cap it.
            int ceiling = (requestedQuality?.Trim().ToLowerInvariant()) switch
            {
                "draft" => 150,
                "normal" => 300,
                _ => MaximumPracticalDpi     // High, Best, or nothing chosen
            };

            return Math.Min(best, ceiling);
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
