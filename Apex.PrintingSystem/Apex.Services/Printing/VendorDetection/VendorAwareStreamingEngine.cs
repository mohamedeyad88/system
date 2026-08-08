using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.Services.Printing.VendorDetection
{
    /// <summary>
    /// Vendor-aware streaming engine that automatically applies
    /// optimized printing strategies based on detected printer vendor.
    /// Operates entirely in the background with no user configuration.
    /// </summary>
    public class VendorAwareStreamingEngine
    {
        private readonly VendorDetectionEngine _vendorDetection;

        // Status messages (non-technical, user-friendly)
        private static readonly string[] StatusMessages = new[]
        {
            "جاري تجهيز الطباعة...",
            "جاري الإرسال إلى الطابعة...",
            "جاري الطباعة...",
            "اكتملت الطباعة بنجاح ✓"
        };

        public VendorAwareStreamingEngine()
        {
            _vendorDetection = VendorDetectionEngine.Instance;
        }

        /// <summary>
        /// Event for non-technical status updates.
        /// </summary>
        public event Action<string>? StatusChanged;

        /// <summary>
        /// Event for progress updates (0-100).
        /// </summary>
        public event Action<int>? ProgressChanged;

        /// <summary>
        /// Stream print data to printer with vendor-optimized settings.
        /// </summary>
        /// <param name="printerName">Target printer name.</param>
        /// <param name="printData">Data to print (PDF bytes, etc.).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if printing succeeded.</returns>
        public async Task<VendorPrintResult> PrintWithVendorOptimizationAsync(
            string printerName,
            byte[] printData,
            CancellationToken cancellationToken = default)
        {
            var result = new VendorPrintResult { PrinterName = printerName };
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Step 1: Silent vendor detection
                UpdateStatus(StatusMessages[0]);
                var metadata = _vendorDetection.GetPrinterMetadata(printerName);
                var profile = VendorProfileFactory.GetProfile(metadata);

                result.Vendor = metadata.Vendor;
                result.ProfileUsed = profile.ProfileName;

                Debug.WriteLine($"[VendorStream] Detected: {metadata.Vendor} for {printerName}");
                Debug.WriteLine($"[VendorStream] Profile: {profile.ProfileName}");

                // Step 2: Stream data with vendor-specific settings
                UpdateStatus(StatusMessages[1]);
                var success = await StreamDataWithProfileAsync(
                    printerName,
                    printData,
                    profile,
                    metadata,
                    cancellationToken);

                if (success)
                {
                    UpdateStatus(StatusMessages[3]);
                    result.Success = true;
                }
                else
                {
                    result.Success = false;
                    result.ErrorMessage = "Print job could not be completed";
                }
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.ErrorMessage = "Print cancelled";
                result.WasCancelled = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VendorStream] Error: {ex.Message}");
                result.Success = false;
                result.ErrorMessage = GetUserFriendlyError(ex);
            }
            finally
            {
                stopwatch.Stop();
                result.ElapsedMs = stopwatch.ElapsedMilliseconds;
            }

            return result;
        }

        /// <summary>
        /// Stream data using vendor-specific profile settings.
        /// </summary>
        private async Task<bool> StreamDataWithProfileAsync(
            string printerName,
            byte[] data,
            VendorPrintProfile profile,
            PrinterMetadata metadata,
            CancellationToken cancellationToken)
        {
            int retryCount = 0;
            bool success = false;

            while (!success && retryCount < profile.MaxRetryAttempts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (retryCount > 0)
                    {
                        Debug.WriteLine($"[VendorStream] Retry {retryCount}/{profile.MaxRetryAttempts}");
                        await Task.Delay(profile.RetryDelayMs, cancellationToken);
                    }

                    success = await ExecutePrintWithProfileAsync(
                        printerName, data, profile, metadata, cancellationToken);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[VendorStream] Attempt {retryCount + 1} failed: {ex.Message}");

                    if (profile.AutoRetrySpoolerErrors && IsSpoolerError(ex))
                    {
                        retryCount++;
                        continue;
                    }

                    // Non-retryable error
                    throw;
                }

                retryCount++;
            }

            return success;
        }

        /// <summary>
        /// Execute printing with vendor-specific optimizations.
        /// </summary>
        private async Task<bool> ExecutePrintWithProfileAsync(
            string printerName,
            byte[] data,
            VendorPrintProfile profile,
            PrinterMetadata metadata,
            CancellationToken cancellationToken)
        {
            UpdateStatus(StatusMessages[2]);

            // Calculate chunk parameters
            int chunkSize = profile.GetChunkSizeBytes();
            int totalChunks = (int)Math.Ceiling((double)data.Length / chunkSize);
            int timeout = profile.GetEffectiveTimeout(metadata.IsNetworkPrinter) * 1000;

            Debug.WriteLine($"[VendorStream] Streaming {data.Length} bytes in {totalChunks} chunks of {chunkSize} bytes");

            // Use appropriate printing method based on profile
            if (profile.PreferRawPrinting && metadata.SupportsDirectPdf)
            {
                return await SendRawDataAsync(printerName, data, chunkSize,
                    profile.ChunkDelayMs, timeout, cancellationToken);
            }
            else
            {
                // Use GDI rendering with chunked approach
                return await RenderAndPrintAsync(printerName, data, profile, cancellationToken);
            }
        }

        /// <summary>
        /// Send raw data directly to printer spooler (HP optimized).
        /// </summary>
        private async Task<bool> SendRawDataAsync(
            string printerName,
            byte[] data,
            int chunkSize,
            int chunkDelayMs,
            int timeoutMs,
            CancellationToken cancellationToken)
        {
            return await Task.Run(async () =>
            {
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(timeoutMs);

                    int totalSent = 0;

                    // Open printer for raw data
                    if (!Helpers.RawPrinterHelper.OpenPrinter(printerName, out var hPrinter))
                        return false;

                    try
                    {
                        // Start document
                        if (!Helpers.RawPrinterHelper.StartDocument(hPrinter, "Apex Print Job"))
                        {
                            Helpers.RawPrinterHelper.ClosePrinter(hPrinter);
                            return false;
                        }

                        // Send data in chunks
                        while (totalSent < data.Length)
                        {
                            cts.Token.ThrowIfCancellationRequested();

                            int remaining = data.Length - totalSent;
                            int currentChunk = Math.Min(chunkSize, remaining);

                            var chunk = new byte[currentChunk];
                            Array.Copy(data, totalSent, chunk, 0, currentChunk);

                            if (!Helpers.RawPrinterHelper.WritePrinter(hPrinter, chunk))
                            {
                                Helpers.RawPrinterHelper.EndDocument(hPrinter);
                                return false;
                            }

                            totalSent += currentChunk;

                            // Report progress
                            int progress = (int)((double)totalSent / data.Length * 100);
                            ProgressChanged?.Invoke(progress);

                            // PRIORITY: Speed - Minimal delay only if absolutely necessary
                            if (chunkDelayMs > 0 && totalSent < data.Length)
                            {
                                // Use async delay instead of blocking Thread.Sleep
                                await Task.Delay(Math.Min(chunkDelayMs, 5), cancellationToken);
                            }
                        }

                        // End document
                        Helpers.RawPrinterHelper.EndDocument(hPrinter);
                        return true;
                    }
                    finally
                    {
                        Helpers.RawPrinterHelper.ClosePrinter(hPrinter);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[VendorStream] Raw print error: {ex.Message}");
                    return false;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Render and print using GDI+ (for non-RAW printers).
        /// </summary>
        private async Task<bool> RenderAndPrintAsync(
            string printerName,
            byte[] data,
            VendorPrintProfile profile,
            CancellationToken cancellationToken)
        {
            // Use PdfDirectPrinter with profile settings
            var pdfPrinter = new PdfDirectPrinter();

            // Create temp file for PDF data
            var tempFile = Path.Combine(Path.GetTempPath(), $"apex_print_{Guid.NewGuid():N}.pdf");

            try
            {
                await File.WriteAllBytesAsync(tempFile, data, cancellationToken);

                // Set timeout based on profile
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(profile.PrinterTimeoutSeconds * 1000);

                var success = await pdfPrinter.PrintPdfAsync(printerName, tempFile, 1, null);

                ProgressChanged?.Invoke(100);
                return success;
            }
            finally
            {
                // Cleanup temp file
                _ = Task.Run(async () =>
                {
                    await Task.Delay(5000);
                    try { File.Delete(tempFile); } catch { }
                });
            }
        }

        /// <summary>
        /// Check if exception is a spooler-related error.
        /// </summary>
        private bool IsSpoolerError(Exception ex)
        {
            var message = ex.Message.ToLowerInvariant();
            return message.Contains("spooler") ||
                   message.Contains("spool") ||
                   message.Contains("rpc") ||
                   message.Contains("printer") ||
                   ex is System.ComponentModel.Win32Exception;
        }

        /// <summary>
        /// Convert technical error to user-friendly message.
        /// </summary>
        private string GetUserFriendlyError(Exception ex)
        {
            var message = ex.Message.ToLowerInvariant();

            if (message.Contains("offline") || message.Contains("not ready"))
                return "الطابعة غير جاهزة";

            if (message.Contains("paper") || message.Contains("media"))
                return "تحقق من الورق في الطابعة";

            if (message.Contains("network") || message.Contains("connection"))
                return "تحقق من اتصال الطابعة بالشبكة";

            if (message.Contains("spooler") || message.Contains("spool"))
                return "حدث خطأ في خدمة الطباعة";

            if (message.Contains("access denied") || message.Contains("permission"))
                return "لا توجد صلاحية للطباعة";

            return "حدث خطأ أثناء الطباعة";
        }

        private void UpdateStatus(string status)
        {
            StatusChanged?.Invoke(status);
        }
    }

    /// <summary>
    /// Result of vendor-aware printing operation.
    /// </summary>
    public class VendorPrintResult
    {
        public bool Success { get; set; }
        public string PrinterName { get; set; } = string.Empty;
        public PrinterVendor Vendor { get; set; }
        public string ProfileUsed { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
        public bool WasCancelled { get; set; }
        public long ElapsedMs { get; set; }

        public override string ToString()
        {
            return Success
                ? $"✓ Printed to {PrinterName} ({Vendor}) in {ElapsedMs}ms"
                : $"✗ Failed: {ErrorMessage}";
        }
    }
}
