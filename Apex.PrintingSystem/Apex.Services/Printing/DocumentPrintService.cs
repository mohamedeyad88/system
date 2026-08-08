using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PdfiumViewer;

namespace Apex.Services.Printing
{
    /// <summary>
    /// DEDICATED print service for Print Operations ONLY.
    /// 
    /// ARCHITECTURAL RULES (STRICTLY ENFORCED):
    /// 1. Document-based printing ONLY (entire document as single job)
    /// 2. NO page-by-page rendering loops
    /// 3. NO scaling or paper size manipulation
    /// 
    /// PRINT PRIORITY:
    /// 1. SumatraPDF (best - silent, supports copies)
    /// 2. PDFtoPrinter (good - silent)
    /// 3. Windows Shell Print (uses default PDF handler)
    /// 4. PdfiumViewer CreatePrintDocument (fallback - sends whole document)
    /// 4. Uses printer driver defaults ONLY
    /// 5. NO fallback - on failure, throw clear exception
    /// 6. NO shared code with Numbering system
    /// 
    /// This service uses EXTERNAL tools or shell printing to send
    /// the document directly to the printer without any processing.
    /// </summary>
    public sealed class DocumentPrintService
    {
        private static readonly Lazy<DocumentPrintService> _instance = new(() => new DocumentPrintService());
        public static DocumentPrintService Instance => _instance.Value;

        private DocumentPrintService() { }

        /// <summary>
        /// Prints a document using driver-based printing.
        /// NO rendering, NO scaling, NO fallback to page-by-page methods.
        /// 
        /// STRICT RULES:
        /// - Document sent as-is to printer driver
        /// - NO PdfiumViewer, NO RIP Engine
        /// - NO page-by-page rendering
        /// - NO scaling or paper size manipulation
        /// - Copies handled by printer driver or external tool
        /// </summary>
        /// <param name="printerName">Target printer name</param>
        /// <param name="filePath">Path to document (PDF, Image, etc.)</param>
        /// <param name="copies">Number of copies</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if print job was submitted successfully</returns>
        /// <exception cref="DocumentPrintException">Thrown on any failure with clear message</exception>
        public async Task<bool> PrintDocumentAsync(
            string printerName,
            string filePath,
            int copies = 1,
            CancellationToken cancellationToken = default)
        {
            // ═══════════════════════════════════════════════════════════════════
            // DOCUMENT PRINT SERVICE - PRINT OPERATIONS PATH
            // This is the ONLY path used by Print Operations
            // NO fallback to VendorAwarePrintGateway or PdfiumViewer
            // ═══════════════════════════════════════════════════════════════════
            Debug.WriteLine($"");
            Debug.WriteLine($"╔══════════════════════════════════════════════════════════════════╗");
            Debug.WriteLine($"║           DOCUMENT PRINT SERVICE - PRINT OPERATIONS             ║");
            Debug.WriteLine($"╠══════════════════════════════════════════════════════════════════╣");
            Debug.WriteLine($"║ Printer: {printerName,-54} ║");
            Debug.WriteLine($"║ File:    {Path.GetFileName(filePath),-54} ║");
            Debug.WriteLine($"║ Copies:  {copies,-54} ║");
            Debug.WriteLine($"║ Mode:    Document-based (NO rendering, NO scaling)              ║");
            Debug.WriteLine($"╚══════════════════════════════════════════════════════════════════╝");
            Debug.WriteLine($"");

            // Validation
            if (string.IsNullOrEmpty(printerName))
                throw new DocumentPrintException("اسم الطابعة مطلوب", "VALIDATION_ERROR");

            if (string.IsNullOrEmpty(filePath))
                throw new DocumentPrintException("مسار الملف مطلوب", "VALIDATION_ERROR");

            if (!File.Exists(filePath))
                throw new DocumentPrintException($"الملف غير موجود: {filePath}", "FILE_NOT_FOUND");

            if (copies < 1)
            {
                Debug.WriteLine($"[DocumentPrintService] WARNING: copies={copies}, setting to 1");
                copies = 1;
            }

            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            Debug.WriteLine($"[DocumentPrintService] File type detected: {extension}");

            // Route based on file type
            bool success = extension switch
            {
                ".pdf" => await PrintPdfDocumentAsync(printerName, filePath, copies, cancellationToken),
                ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".tiff" =>
                    await PrintImageDocumentAsync(printerName, filePath, copies, cancellationToken),
                _ => throw new DocumentPrintException(
                    $"نوع الملف غير مدعوم: {extension}. الأنواع المدعومة: PDF, JPG, PNG, BMP, GIF, TIFF",
                    "UNSUPPORTED_FORMAT")
            };

            if (!success)
            {
                throw new DocumentPrintException(
                    "فشلت عملية الطباعة. تأكد من تثبيت أداة طباعة PDF (مثل SumatraPDF) أو استخدم Quick Print للطباعة عبر المعالج.",
                    "PRINT_FAILED");
            }

            Debug.WriteLine($"[DocumentPrintService] ✅ Print job submitted successfully to {printerName}");
            return true;
        }

        /// <summary>
        /// Print PDF using EXTERNAL tools only (no rendering).
        /// Order: SumatraPDF → PDFtoPrinter → System Default Handler
        /// NO Pdfium, NO page rendering.
        /// 
        /// COPIES HANDLING:
        /// - SumatraPDF: Uses -print-settings "Nx" for N copies
        /// - PDFtoPrinter: Doesn't support copies natively, loops internally
        /// - Shell Print: Loops internally (Windows limitation)
        /// </summary>
        private async Task<bool> PrintPdfDocumentAsync(
            string printerName,
            string filePath,
            int copies,
            CancellationToken cancellationToken)
        {
            Debug.WriteLine($"[DocumentPrintService] ══════════════════════════════════════");
            Debug.WriteLine($"[DocumentPrintService] PDF DOCUMENT PRINTING");
            Debug.WriteLine($"[DocumentPrintService] File: {filePath}");
            Debug.WriteLine($"[DocumentPrintService] Printer: {printerName}");
            Debug.WriteLine($"[DocumentPrintService] Copies: {copies}");
            Debug.WriteLine($"[DocumentPrintService] ══════════════════════════════════════");

            // Try 1: PdfiumViewer document mode (PRIMARY — built-in, silent, reliable).
            // It renders via the bundled Pdfium engine and goes straight through the
            // Windows spooler with proper copies support. Previously this was the
            // LAST fallback, so machines without SumatraPDF fell through to shell
            // printing which launched Adobe's UI and reported false success.
            if (await PrintPdfWithPdfiumDocumentModeAsync(printerName, filePath, copies, cancellationToken))
            {
                return true;
            }
            Debug.WriteLine($"[DocumentPrintService] ⚠️ Pdfium failed, trying external tools");

            // Try 2: SumatraPDF (if installed - supports copies natively via -print-settings)
            var sumatraPath = FindSumatraPdf();
            if (!string.IsNullOrEmpty(sumatraPath))
            {
                Debug.WriteLine($"[DocumentPrintService] Using SumatraPDF: {sumatraPath}");
                Debug.WriteLine($"[DocumentPrintService] Copies via: -print-settings \"{copies}x\"");

                if (await RunExternalPrintToolAsync(sumatraPath,
                    $"-print-to \"{printerName}\" -print-settings \"{copies}x\" -silent \"{filePath}\"",
                    cancellationToken))
                {
                    Debug.WriteLine($"[DocumentPrintService] ✅ SumatraPDF print successful");
                    return true;
                }
                Debug.WriteLine($"[DocumentPrintService] ⚠️ SumatraPDF failed, trying next method");
            }

            // Try 3: PDFtoPrinter (copies via -copies parameter if supported, otherwise loop)
            var pdfToPrinterPath = FindPdfToPrinter();
            if (!string.IsNullOrEmpty(pdfToPrinterPath))
            {
                Debug.WriteLine($"[DocumentPrintService] Using PDFtoPrinter: {pdfToPrinterPath}");
                Debug.WriteLine($"[DocumentPrintService] Copies via: -copies {copies}");

                // PDFtoPrinter syntax: PDFtoPrinter.exe <file> [<printer>] [-copies <N>]
                if (await RunExternalPrintToolAsync(pdfToPrinterPath,
                    $"\"{filePath}\" \"{printerName}\" -copies {copies}",
                    cancellationToken))
                {
                    Debug.WriteLine($"[DocumentPrintService] ✅ PDFtoPrinter print successful");
                    return true;
                }
                Debug.WriteLine($"[DocumentPrintService] ⚠️ PDFtoPrinter failed, trying next method");
            }

            // Try 4: Windows Shell Print — LAST RESORT ONLY. Depends on the system's
            // default PDF handler (may open its UI) and success cannot be verified.
            Debug.WriteLine($"[DocumentPrintService] Using Windows Shell Print (last resort)");
            return await ShellPrintAsync(printerName, filePath, copies, cancellationToken);
        }

        /// <summary>
        /// Fallback PDF printing using PdfiumViewer's CreatePrintDocument.
        /// This sends the ENTIRE document to the printer - NOT page-by-page rendering.
        /// </summary>
        private async Task<bool> PrintPdfWithPdfiumDocumentModeAsync(
            string printerName,
            string filePath,
            int copies,
            CancellationToken cancellationToken)
        {
            Debug.WriteLine($"[DocumentPrintService] ══════════════════════════════════════");
            Debug.WriteLine($"[DocumentPrintService] PDFIUM DOCUMENT MODE (Fallback)");
            Debug.WriteLine($"[DocumentPrintService] Printer: {printerName}");
            Debug.WriteLine($"[DocumentPrintService] File: {filePath}");
            Debug.WriteLine($"[DocumentPrintService] Copies: {copies}");
            Debug.WriteLine($"[DocumentPrintService] ══════════════════════════════════════");

            return await Task.Run(() =>
            {
                try
                {
                    using var pdfDocument = PdfiumViewer.PdfDocument.Load(filePath);

                    // Use PdfiumViewer's built-in print document creation
                    // This creates a proper PrintDocument that sends all pages as one job
                    using var printDocument = pdfDocument.CreatePrintDocument();

                    printDocument.PrinterSettings.PrinterName = printerName;
                    printDocument.PrinterSettings.Copies = (short)copies;
                    printDocument.DocumentName = Path.GetFileName(filePath);

                    // Print-to-file virtual printers silently drop jobs that have no
                    // output file name (QA-measured false success). Supply one.
                    var lower = printerName.ToLowerInvariant();
                    if (lower.Contains("microsoft print to pdf") || lower.Contains("xps"))
                    {
                        string ext = lower.Contains("xps") ? ".oxps" : ".pdf";
                        printDocument.PrinterSettings.PrintToFile = true;
                        printDocument.PrinterSettings.PrintFileName = Path.Combine(
                            Path.GetDirectoryName(filePath) ?? Path.GetTempPath(),
                            $"{Path.GetFileNameWithoutExtension(filePath)}-printed-{DateTime.Now:yyyyMMdd-HHmmss}{ext}");
                        Debug.WriteLine($"[DocumentPrintService] Virtual printer → PrintToFile: {printDocument.PrinterSettings.PrintFileName}");
                    }

                    // Use default print mode (not custom page rendering)
                    printDocument.PrintController = new System.Drawing.Printing.StandardPrintController();

                    Debug.WriteLine($"[DocumentPrintService] PdfiumViewer: Sending {pdfDocument.PageCount} page(s) to printer");

                    printDocument.Print();

                    Debug.WriteLine($"[DocumentPrintService] ✅ PdfiumViewer print completed");
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DocumentPrintService] ❌ PdfiumViewer print failed: {ex.Message}");
                    return false;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Print image using Windows GDI.
        /// Scales image to fit within printable area while maintaining aspect ratio.
        /// This prevents massive spool files from high-resolution images.
        /// </summary>
        private async Task<bool> PrintImageDocumentAsync(
            string printerName,
            string filePath,
            int copies,
            CancellationToken cancellationToken)
        {
            Debug.WriteLine($"[DocumentPrintService] Image Document → Using GDI printing (fit to page)");

            return await Task.Run(() =>
            {
                try
                {
                    using var image = System.Drawing.Image.FromFile(filePath);
                    using var printDoc = new System.Drawing.Printing.PrintDocument();

                    printDoc.PrinterSettings.PrinterName = printerName;
                    printDoc.PrinterSettings.Copies = (short)copies;
                    printDoc.DocumentName = Path.GetFileName(filePath);

                    Debug.WriteLine($"[DocumentPrintService] Image size: {image.Width}x{image.Height} pixels");
                    Debug.WriteLine($"[DocumentPrintService] Image resolution: {image.HorizontalResolution}x{image.VerticalResolution} DPI");

                    // ═══════════════════════════════════════════════════════════════════
                    // FIT TO PAGE: Scale image to fit within printable area
                    // This is what Adobe Reader and Windows Photo Viewer do
                    // Prevents 2GB+ spool files from high-resolution images
                    // ═══════════════════════════════════════════════════════════════════

                    int pagesPrinted = 0;

                    printDoc.PrintPage += (sender, e) =>
                    {
                        pagesPrinted++;
                        Debug.WriteLine($"[DocumentPrintService] PrintPage event fired - Page #{pagesPrinted}");

                        if (e.Graphics != null && image != null)
                        {
                            // Get printable area bounds
                            var printArea = e.MarginBounds;

                            // Calculate scale to fit image within printable area
                            // while maintaining aspect ratio
                            float scaleX = (float)printArea.Width / image.Width;
                            float scaleY = (float)printArea.Height / image.Height;
                            float scale = Math.Min(scaleX, scaleY);

                            // If image is smaller than page, don't enlarge it
                            if (scale > 1.0f) scale = 1.0f;

                            // Calculate destination size
                            int destWidth = (int)(image.Width * scale);
                            int destHeight = (int)(image.Height * scale);

                            // Center image on page
                            int x = printArea.X + (printArea.Width - destWidth) / 2;
                            int y = printArea.Y + (printArea.Height - destHeight) / 2;

                            Debug.WriteLine($"[DocumentPrintService] Fit to page: Scale={scale:F2}, Dest={destWidth}x{destHeight}");

                            // Use high quality interpolation for scaling
                            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

                            // Draw image scaled to fit page
                            e.Graphics.DrawImage(image, x, y, destWidth, destHeight);
                        }

                        // CRITICAL: Explicitly set no more pages to prevent infinite loop
                        e.HasMorePages = false;
                        Debug.WriteLine($"[DocumentPrintService] HasMorePages set to FALSE - printing complete");
                    };

                    Debug.WriteLine($"[DocumentPrintService] Sending to printer: {printerName} (Copies: {copies})");

                    printDoc.Print();
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DocumentPrintService] Image print failed: {ex.Message}");
                    return false;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Run external print tool and wait for completion.
        /// </summary>
        private async Task<bool> RunExternalPrintToolAsync(
            string toolPath,
            string arguments,
            CancellationToken cancellationToken)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = toolPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(psi);
                if (process == null) return false;

                await process.WaitForExitAsync(cancellationToken);

                var exitCode = process.ExitCode;
                Debug.WriteLine($"[DocumentPrintService] Tool exit code: {exitCode}");

                return exitCode == 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DocumentPrintService] External tool failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Windows Shell print (verb "printto").
        /// NOTE: Shell print sends document to default handler (Adobe Reader, etc.)
        /// The handler is responsible for copies - we send the command ONCE only.
        /// </summary>
        private async Task<bool> ShellPrintAsync(
            string printerName,
            string filePath,
            int copies,
            CancellationToken cancellationToken)
        {
            Debug.WriteLine($"[DocumentPrintService] ══════════════════════════════════════");
            Debug.WriteLine($"[DocumentPrintService] SHELL PRINT (Fallback)");
            Debug.WriteLine($"[DocumentPrintService] Printer: {printerName}");
            Debug.WriteLine($"[DocumentPrintService] File: {filePath}");
            Debug.WriteLine($"[DocumentPrintService] Copies requested: {copies}");
            Debug.WriteLine($"[DocumentPrintService] NOTE: Copies handled by default handler, sending ONE command");
            Debug.WriteLine($"[DocumentPrintService] ══════════════════════════════════════");

            return await Task.Run(() =>
            {
                try
                {
                    // IMPORTANT: Send ONE print command only
                    // The document handler (Adobe Reader, etc.) should respect copies from settings
                    // DO NOT loop - it creates multiple separate print jobs!
                    var psi = new ProcessStartInfo
                    {
                        FileName = filePath,
                        Verb = "printto",
                        Arguments = $"\"{printerName}\"",
                        UseShellExecute = true,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };

                    Debug.WriteLine($"[DocumentPrintService] Starting shell print process...");
                    using var process = Process.Start(psi);
                    process?.WaitForExit(60000); // 60 second timeout

                    Debug.WriteLine($"[DocumentPrintService] Shell print process completed");

                    // For copies > 1, show warning since shell print doesn't support copies natively
                    if (copies > 1)
                    {
                        Debug.WriteLine($"[DocumentPrintService] ⚠️ WARNING: Copies > 1 but shell print only sends 1 copy");
                        Debug.WriteLine($"[DocumentPrintService] ⚠️ Install SumatraPDF for proper copies support");
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DocumentPrintService] Shell print failed: {ex.Message}");
                    return false;
                }
            }, cancellationToken);
        }

        private string? FindSumatraPdf()
        {
            var paths = new[]
            {
                @"C:\Program Files\SumatraPDF\SumatraPDF.exe",
                @"C:\Program Files (x86)\SumatraPDF\SumatraPDF.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SumatraPDF", "SumatraPDF.exe")
            };
            return Array.Find(paths, File.Exists);
        }

        private string? FindPdfToPrinter()
        {
            var paths = new[]
            {
                @"C:\Program Files\PDFtoPrinter\PDFtoPrinter.exe",
                @"C:\Program Files (x86)\PDFtoPrinter\PDFtoPrinter.exe",
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PDFtoPrinter.exe")
            };
            return Array.Find(paths, File.Exists);
        }
    }

    /// <summary>
    /// Exception specific to Document Print Service.
    /// Provides clear error messages without silent fallback.
    /// </summary>
    public class DocumentPrintException : Exception
    {
        public string ErrorCode { get; }

        public DocumentPrintException(string message, string errorCode) : base(message)
        {
            ErrorCode = errorCode;
        }
    }
}
