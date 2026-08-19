using Apex.Core.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Apex.Services.Printing.RIP;

namespace Apex.Services.Printing.VendorDetection
{
    /// <summary>
    /// The MANDATORY gateway for all print operations.
    /// Every print command MUST pass through this gateway.
    /// Automatically applies vendor-specific optimizations.
    /// 
    /// This is the single entry point that:
    /// 1. Detects the printer vendor silently
    /// 2. Selects the appropriate vendor profile
    /// 3. Applies optimized printing strategy
    /// 4. Handles errors with vendor-aware recovery
    /// </summary>
    public class VendorAwarePrintGateway
    {
        private static readonly Lazy<VendorAwarePrintGateway> _instance =
            new(() => new VendorAwarePrintGateway());

        public static VendorAwarePrintGateway Instance => _instance.Value;

        private readonly VendorDetectionEngine _detection;
        private readonly VendorAwareStreamingEngine _streamingEngine;

        private VendorAwarePrintGateway()
        {
            _detection = VendorDetectionEngine.Instance;
            _streamingEngine = new VendorAwareStreamingEngine();
        }

        /// <summary>
        /// Event for status updates (user-friendly, non-technical).
        /// </summary>
        public event Action<string>? StatusChanged;

        /// <summary>
        /// Event for progress updates (0-100).
        /// </summary>
        public event Action<int>? ProgressChanged;

        /// <summary>
        /// IMPORTANT: For high-volume printing (20+ jobs), use the Queue system instead:
        /// 
        /// var queueManager = PrintJobQueueManager.Instance;
        /// queueManager.Start();
        /// await queueManager.SubmitPrintJobAsync(printerName, filePath, copies);
        /// 
        /// Queue system benefits:
        /// - Automatic throttling (no network storms)
        /// - One job per printer at a time
        /// - Built-in retry logic
        /// - Network-safe under burst load
        /// - Printer lock management
        /// 
        /// Direct PrintAsync() should only be used for:
        /// - Single urgent jobs
        /// - Testing/diagnostics
        /// - Legacy compatibility
        /// </summary>

        /// <summary>
        /// Print a file through the vendor-aware pipeline.
        /// This is the ONLY method that should be used for printing.
        /// </summary>
        /// <param name="printerName">Target printer name.</param>
        /// <param name="filePath">Path to file to print.</param>
        /// <param name="copies">Number of copies.</param>
        /// <param name="settings">Optional print settings.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Print result with details.</returns>
        public async Task<GatewayPrintResult> PrintAsync(
            string printerName,
            string filePath,
            int copies = 1,
            PrintJob? settings = null,
            CancellationToken cancellationToken = default,
            bool documentMode = false)
        {
            var result = new GatewayPrintResult
            {
                PrinterName = printerName,
                FilePath = filePath,
                Copies = copies
            };

            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Validation
                if (string.IsNullOrEmpty(printerName))
                    throw new ArgumentException("Printer name is required", nameof(printerName));

                if (!File.Exists(filePath))
                    throw new FileNotFoundException("File not found", filePath);

                // Step 1: Detect vendor and get profile
                UpdateStatus("جاري تحضير الطابعة...");
                var metadata = _detection.GetPrinterMetadata(printerName);
                var profile = VendorProfileFactory.GetProfile(metadata);

                result.Vendor = metadata.Vendor;
                result.ProfileUsed = profile.ProfileName;
                result.IsNetworkPrinter = metadata.IsNetworkPrinter;

                Debug.WriteLine($"[Gateway] Printer: {printerName}");
                Debug.WriteLine($"[Gateway] Vendor: {metadata.Vendor} (confidence: {metadata.DetectionConfidence}%)");
                Debug.WriteLine($"[Gateway] Profile: {profile.ProfileName}");
                Debug.WriteLine($"[Gateway] Connection: {metadata.ConnectionType}");

                // Step 2: Determine print method based on file type
                var extension = Path.GetExtension(filePath).ToLowerInvariant();

                // Print-to-file virtual printers (Microsoft Print to PDF / XPS):
                // the streaming/RIP engines report success while the spooler silently
                // drops jobs that carry no output file name (QA-measured false
                // success). Route them through PdfDirectPrinter, whose Pdfium path
                // sets PrintToFile + an auto-derived PrintFileName.
                var plower = printerName.ToLowerInvariant();
                bool isVirtualPtf = plower.Contains("microsoft print to pdf") || plower.Contains("xps");

                bool success;
                if (extension == ".pdf" && isVirtualPtf)
                {
                    Debug.WriteLine("[Gateway] Virtual print-to-file printer → PdfDirectPrinter (PrintToFile route)");
                    UpdateStatus("طباعة إلى ملف...");
                    success = await new PdfDirectPrinter().PrintPdfAsync(
                        printerName, filePath, copies, settings);
                }
                else if (extension == ".pdf")
                {
                    success = await PrintPdfWithVendorProfileAsync(
                        printerName, filePath, copies, profile, metadata, settings, cancellationToken, documentMode);
                }
                else if (IsImageFile(extension))
                {
                    success = await PrintImageWithVendorProfileAsync(
                        printerName, filePath, copies, profile, metadata, cancellationToken);
                }
                else
                {
                    // Use PdfDirectPrinter for other files (after conversion)
                    success = await PrintGenericFileAsync(
                        printerName, filePath, copies, profile, metadata, cancellationToken);
                }

                result.Success = success;

                if (success)
                {
                    UpdateStatus("اكتملت الطباعة ✓");
                    UpdateProgress(100);
                }
                else
                {
                    result.ErrorMessage = "فشلت عملية الطباعة";
                }
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.ErrorMessage = "تم إلغاء الطباعة";
                result.WasCancelled = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Gateway] Error: {ex}");
                result.Success = false;
                result.ErrorMessage = GetFriendlyErrorMessage(ex);
            }
            finally
            {
                stopwatch.Stop();
                result.ElapsedMs = stopwatch.ElapsedMilliseconds;
            }

            return result;
        }

        /// <summary>
        /// Print PDF with vendor-optimized settings using RIP Engine.
        /// </summary>
        private async Task<bool> PrintPdfWithVendorProfileAsync(
            string printerName,
            string pdfPath,
            int copies,
            VendorPrintProfile profile,
            PrinterMetadata metadata,
            PrintJob? settings,
            CancellationToken cancellationToken,
            bool documentMode)
        {
            // ═══════════════════════════════════════════════════════════════════
            // ARCHITECTURAL RULE: VendorAwarePrintGateway is for QUICK PRINT only.
            // Print Operations should use DocumentPrintService directly.
            // If documentMode=true reaches here, log warning and reject.
            // ═══════════════════════════════════════════════════════════════════
            if (documentMode)
            {
                Debug.WriteLine("[Gateway] ⚠️ WARNING: documentMode=true reached VendorAwarePrintGateway");
                Debug.WriteLine("[Gateway] This should have been handled by DocumentPrintService");
                Debug.WriteLine("[Gateway] Rejecting to enforce architectural separation");
                return false;  // Force caller to use correct path
            }

            Debug.WriteLine("[Gateway] ══════════════════════════════════════");
            Debug.WriteLine("[Gateway] QUICK PRINT PATH (VendorAwarePrintGateway)");
            Debug.WriteLine("[Gateway] Using: RIP Engine + Pdfium fallback");
            Debug.WriteLine("[Gateway] ══════════════════════════════════════");

            UpdateStatus("جاري تحليل المحتوى...");

            // Use Universal RIP Engine for intelligent rendering (capability-based, not vendor-based)
            var universalEngine = new Apex.Services.Printing.UniversalRIP.UniversalRipEngine();

            // Determine quality level based on printer capabilities
            var qualityLevel = Apex.Services.Printing.RIP.Models.QualityLevel.Professional;
            if (metadata.Capabilities?.MaxDpi >= 1200)
                qualityLevel = Apex.Services.Printing.RIP.Models.QualityLevel.Industrial;
            else if (metadata.Capabilities?.MaxDpi < 600)
                qualityLevel = Apex.Services.Printing.RIP.Models.QualityLevel.Standard;

            // Apply vendor-specific settings
            int retries = 0;
            bool success = false;

            while (!success && retries < profile.MaxRetryAttempts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (retries > 0)
                {
                    Debug.WriteLine($"[Gateway] Retry {retries}/{profile.MaxRetryAttempts}");
                    // PRIORITY: Speed - Minimal retry delay (only 100ms)
                    await Task.Delay(Math.Min(profile.RetryDelayMs, 100), cancellationToken);
                }

                try
                {
                    // ═══════════════════════════════════════════════════════════════════
                    // The vendor's own Windows driver is the primary path for every
                    // printer, not a special case for one Epson model.
                    //
                    // The driver rasterises at the device's real resolution and honours
                    // the duplex/colour/tray/paper settings the user chose. The RIP
                    // pipeline below cannot: it rasterises pages itself and emits one
                    // spooler job per page. It stays as a fallback for devices the
                    // driver path cannot drive.
                    // ═══════════════════════════════════════════════════════════════════
                    UpdateStatus("جاري الطباعة عبر تعريف الطابعة...");

                    var pdfPrinter = new Apex.Services.Printing.PdfDirectPrinter();
                    success = await pdfPrinter.PrintPdfAsync(printerName, pdfPath, copies, settings);

                    if (!success)
                    {
                        Apex.Services.Logging.PrintLogger.Warning(
                            "[Gateway] Driver path did not print on '{Printer}'. Falling back to the RIP pipeline.",
                            printerName);

                        UpdateStatus("جاري الطباعة باستخدام Universal RIP Engine...");

                        var universalResult = await universalEngine.PrintPdfAsync(
                            pdfPath,
                            printerName,
                            copies,
                            qualityLevel,
                            cancellationToken);

                        success = universalResult.Success;

                        if (!success)
                        {
                            Apex.Services.Logging.PrintLogger.Error(
                                "[Gateway] Both paths failed on '{Printer}'. RIP reported: {Reason} (pages printed: {Printed}, failed: {Failed})",
                                printerName,
                                universalResult.ErrorMessage ?? "no reason recorded",
                                universalResult.PagesPrinted,
                                universalResult.FailedPages.Count);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Gateway] RIP print attempt {retries + 1} failed: {ex.Message}");

                    // PRIORITY: Fix printing failures - Always try fallback
                    if (retries >= profile.MaxRetryAttempts - 1 || !profile.AutoRetrySpoolerErrors)
                    {
                        // Fallback to standard printing (more reliable)
                        Debug.WriteLine("[Gateway] Falling back to standard printing...");
                        try
                        {
                            var pdfPrinter = new Apex.Services.Printing.PdfDirectPrinter();
                            success = await pdfPrinter.PrintPdfAsync(printerName, pdfPath, copies, settings);
                            if (success) break; // Success with fallback
                        }
                        catch (Exception fallbackEx)
                        {
                            Debug.WriteLine($"[Gateway] Fallback also failed: {fallbackEx.Message}");
                        }
                    }
                }

                retries++;
            }

            // Progress update
            if (success)
                UpdateProgress(100);

            return success;
        }

        /// <summary>
        /// Print image with vendor-optimized settings.
        /// </summary>
        private async Task<bool> PrintImageWithVendorProfileAsync(
            string printerName,
            string imagePath,
            int copies,
            VendorPrintProfile profile,
            PrinterMetadata metadata,
            CancellationToken cancellationToken)
        {
            UpdateStatus("جاري طباعة الصورة...");

            return await Task.Run(() =>
            {
                try
                {
                    using var image = System.Drawing.Image.FromFile(imagePath);
                    using var pd = new System.Drawing.Printing.PrintDocument();

                    pd.PrinterSettings.PrinterName = printerName;
                    pd.PrinterSettings.Copies = (short)copies;
                    pd.DocumentName = Path.GetFileName(imagePath);

                    pd.PrintPage += (s, e) =>
                    {
                        if (e.Graphics != null)
                        {
                            e.Graphics.DrawImage(image, e.MarginBounds);
                        }
                    };

                    pd.Print();
                    UpdateProgress(100);
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Gateway] Image print failed: {ex.Message}");
                    return false;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Print generic file using text rendering.
        /// </summary>
        private async Task<bool> PrintGenericFileAsync(
            string printerName,
            string filePath,
            int copies,
            VendorPrintProfile profile,
            PrinterMetadata metadata,
            CancellationToken cancellationToken)
        {
            UpdateStatus("جاري الطباعة...");

            return await Task.Run(() =>
            {
                try
                {
                    var text = File.ReadAllText(filePath);
                    using var pd = new System.Drawing.Printing.PrintDocument();

                    pd.PrinterSettings.PrinterName = printerName;
                    pd.PrinterSettings.Copies = (short)copies;
                    pd.DocumentName = Path.GetFileName(filePath);

                    var lines = text.Split('\n');
                    int lineIndex = 0;
                    int linesPerPage = 50;

                    pd.PrintPage += (s, e) =>
                    {
                        if (e.Graphics == null) return;

                        using var font = new System.Drawing.Font("Consolas", 10);
                        float y = e.MarginBounds.Top;
                        float lineHeight = font.GetHeight(e.Graphics);
                        int printed = 0;

                        while (lineIndex < lines.Length && printed < linesPerPage)
                        {
                            e.Graphics.DrawString(lines[lineIndex], font,
                                System.Drawing.Brushes.Black, e.MarginBounds.Left, y);
                            y += lineHeight;
                            lineIndex++;
                            printed++;
                        }

                        e.HasMorePages = lineIndex < lines.Length;
                    };

                    pd.Print();
                    UpdateProgress(100);
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Gateway] Generic print failed: {ex.Message}");
                    return false;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Check if file is an image.
        /// </summary>
        private bool IsImageFile(string extension)
        {
            return extension is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".tiff";
        }

        /// <summary>
        /// Get user-friendly error message.
        /// </summary>
        private string GetFriendlyErrorMessage(Exception ex)
        {
            var msg = ex.Message.ToLowerInvariant();

            if (msg.Contains("offline"))
                return "الطابعة غير متصلة";
            if (msg.Contains("paper"))
                return "تحقق من الورق في الطابعة";
            if (msg.Contains("network") || msg.Contains("connection"))
                return "تحقق من اتصال الشبكة";
            if (msg.Contains("access denied"))
                return "لا توجد صلاحية للطباعة";
            if (msg.Contains("spooler"))
                return "مشكلة في خدمة الطباعة";

            return "حدث خطأ أثناء الطباعة";
        }

        private void UpdateStatus(string status)
        {
            StatusChanged?.Invoke(status);
        }

        private void UpdateProgress(int progress)
        {
            ProgressChanged?.Invoke(progress);
        }

        /// <summary>
        /// Get information about all detected printers.
        /// </summary>
        public async Task<System.Collections.Generic.IReadOnlyList<PrinterMetadata>> GetAllPrintersAsync()
        {
            return await _detection.DetectAllPrintersAsync();
        }

        /// <summary>
        /// Get vendor profile for a specific printer (for internal use).
        /// </summary>
        public VendorPrintProfile GetPrinterProfile(string printerName)
        {
            var metadata = _detection.GetPrinterMetadata(printerName);
            return VendorProfileFactory.GetProfile(metadata);
        }
    }

    /// <summary>
    /// Result of gateway print operation.
    /// </summary>
    public class GatewayPrintResult
    {
        public bool Success { get; set; }
        public string PrinterName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public int Copies { get; set; }
        public PrinterVendor Vendor { get; set; }
        public string ProfileUsed { get; set; } = string.Empty;
        public bool IsNetworkPrinter { get; set; }
        public string? ErrorMessage { get; set; }
        public bool WasCancelled { get; set; }
        public long ElapsedMs { get; set; }
    }
}
