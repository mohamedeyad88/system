using Apex.Services.Printing.RIP.Models;
using Apex.Services.Printing.RIP;
using Apex.Services.Printing.UniversalRIP;
using Apex.Services.Printing.VendorDetection;
using Apex.Services.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.Services.Printing.UniversalRIP
{
    /// <summary>
    /// Universal Smart Printing Engine - Industrial RIP-like system.
    /// 
    /// ARCHITECTURE:
    /// Input File → Content Analysis → Printer Detection → Decision Engine → 
    /// Safe Output Strategy → Quality Gate → Printer Delivery
    /// 
    /// GOVERNING PRINCIPLE: Printers are supported by capabilities, not by brand names.
    /// </summary>
    public class UniversalRipEngine
    {
        private readonly EnhancedContentAnalysisEngine _contentAnalyzer;
        private readonly UniversalPrinterCapabilityDetector _capabilityDetector;
        private readonly UniversalDecisionEngine _decisionEngine;
        private readonly HybridRenderStrategy _renderStrategy;
        private readonly SafeOutputStrategy _outputStrategy;
        private readonly QualityGate _qualityGate;
        private readonly FailureRecoverySystem _recoverySystem;

        public UniversalRipEngine()
        {
            _contentAnalyzer = new EnhancedContentAnalysisEngine();
            _capabilityDetector = new UniversalPrinterCapabilityDetector();
            _decisionEngine = new UniversalDecisionEngine();
            _renderStrategy = new HybridRenderStrategy();
            _outputStrategy = new SafeOutputStrategy();
            _qualityGate = new QualityGate();
            _recoverySystem = new FailureRecoverySystem();
        }

        /// <summary>
        /// Print a PDF file using Universal RIP processing.
        /// This is the MAIN entry point for all printing operations.
        /// </summary>
        public async Task<UniversalPrintResult> PrintPdfAsync(
            string pdfPath,
            string printerName,
            int copies = 1,
            QualityLevel qualityLevel = QualityLevel.Professional,
            CancellationToken cancellationToken = default)
        {
            var result = new UniversalPrintResult
            {
                PrinterName = printerName,
                FilePath = pdfPath
            };

            try
            {
                // ═══════════════════════════════════════════════════════════════════
                // STAGE 1: Content Analysis (MANDATORY)
                // ═══════════════════════════════════════════════════════════════════
                Debug.WriteLine("[UniversalRIP] Stage 1: Analyzing content...");
                var representativeProfile = await _contentAnalyzer.AnalyzeSampleAsync(
                    pdfPath,
                    samplePages: 3,
                    cancellationToken);

                // ═══════════════════════════════════════════════════════════════════
                // STAGE 2: Printer Capability Detection (MANDATORY)
                // ═══════════════════════════════════════════════════════════════════
                Debug.WriteLine("[UniversalRIP] Stage 2: Detecting printer capabilities...");
                var printerProfile = await _capabilityDetector.DetectAsync(printerName);

                result.PrinterProfile = printerProfile;
                result.DetectedLanguage = printerProfile.PrimaryLanguage;

                // ═══════════════════════════════════════════════════════════════════
                // STAGE 3: Universal Decision Engine (CORE LOGIC)
                // ═══════════════════════════════════════════════════════════════════
                Debug.WriteLine("[UniversalRIP] Stage 3: Making rendering decision...");
                var decision = _decisionEngine.MakeDecision(
                    representativeProfile,
                    printerProfile,
                    qualityLevel);

                result.Decision = decision;
                Debug.WriteLine($"[UniversalRIP] Decision: {decision.Strategy}, Language: {decision.OutputLanguage}, DPI: {decision.RequiredDpi}");

                // ═══════════════════════════════════════════════════════════════════
                // STAGE 4: Process and Print (Page-by-Page)
                // ═══════════════════════════════════════════════════════════════════
                return await ProcessWithUniversalPipelineAsync(
                    pdfPath,
                    printerName,
                    printerProfile,
                    decision,
                    copies,
                    result,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                PrintLogger.Error(ex,
                    "[UniversalRIP] Setup failed for '{Printer}' before any page was processed. Attempting recovery.",
                    printerName);

                result.Success = false;
                result.ErrorMessage = $"{ex.GetType().Name}: {ex.Message}";

                // Attempt recovery
                return await _recoverySystem.AttemptRecoveryAsync(
                    pdfPath,
                    printerName,
                    ex,
                    result,
                    cancellationToken);
            }
        }

        #region Universal Pipeline Processing

        private async Task<UniversalPrintResult> ProcessWithUniversalPipelineAsync(
            string pdfPath,
            string printerName,
            UniversalPrinterProfile printerProfile,
            UniversalRenderDecision decision,
            int copies,
            UniversalPrintResult result,
            CancellationToken cancellationToken)
        {
            return await Task.Run(async () =>
            {
                try
                {
                    using var pdfDocument = PdfiumViewer.PdfDocument.Load(pdfPath);
                    int pageCount = pdfDocument.PageCount;
                    result.TotalPages = pageCount;

                    // ═══════════════════════════════════════════════════════════════════
                    // CRITICAL LOGGING: Track page count
                    // ═══════════════════════════════════════════════════════════════════
                    PrintLogger.Warning(
                        "[UniversalRIP] ========== PDF HAS {PageCount} PAGES ==========",
                        pageCount);

                    // Process each page
                    for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        PrintLogger.Warning(
                            "[UniversalRIP] ========== PROCESSING PAGE {Page} of {Total} ==========",
                            pageIndex + 1, pageCount);

                        // Analyze this specific page
                        var pageProfile = await _contentAnalyzer.AnalyzePageAsync(
                            pdfPath,
                            pageIndex,
                            cancellationToken);

                        // Make page-specific decision
                        var pageDecision = _decisionEngine.MakeDecision(
                            pageProfile,
                            printerProfile,
                            decision.QualityLevel);

                        // ═══════════════════════════════════════════════════════════════════
                        // QUALITY GATE: Validate before rendering
                        // ═══════════════════════════════════════════════════════════════════
                        if (!_qualityGate.ValidateBeforeRender(pageProfile, pageDecision, printerProfile))
                        {
                            Debug.WriteLine($"[UniversalRIP] Quality Gate failed for page {pageIndex}, using safe fallback");
                            pageDecision = _decisionEngine.MakeDecision(
                                pageProfile,
                                printerProfile,
                                QualityLevel.Standard); // Use safer quality level
                        }

                        // Render page
                        Apex.Services.Printing.RIP.RenderResult? renderResult = null;
                        try
                        {
                            // Everything from here to the matching catch is one page.
                            // It used to sit inside the whole-document try, so any
                            // failure here threw away every remaining page.
                            renderResult = await _renderStrategy.RenderPageAsync(
                                pdfPath,
                                pageIndex,
                                ConvertToRenderDecision(pageDecision),
                                pageProfile,
                                cancellationToken);

                            // ═══════════════════════════════════════════════════════════════════
                            // QUALITY GATE: Validate after rendering
                            // ═══════════════════════════════════════════════════════════════════
                            if (!_qualityGate.ValidateAfterRender(renderResult, pageDecision))
                            {
                                Debug.WriteLine($"[UniversalRIP] Post-render Quality Gate failed for page {pageIndex}");
                                renderResult.Dispose();
                                renderResult = null;

                                // Retry with fallback strategy
                                pageDecision.Strategy = RenderStrategy.FallbackRaster;
                                pageDecision.RequiredDpi = 300;
                                renderResult = await _renderStrategy.RenderPageAsync(
                                    pdfPath,
                                    pageIndex,
                                    ConvertToRenderDecision(pageDecision),
                                    pageProfile,
                                    cancellationToken);
                            }

                            // Generate safe output
                            var universalRenderResult = new RenderResult
                            {
                                RasterImage = renderResult.RasterizedImage,
                                SourcePdfPath = pdfPath,
                                IsNativeVector = renderResult.RequiresNativeOutput
                            };

                            var outputBytes = await _outputStrategy.GenerateSafeOutputAsync(
                                universalRenderResult,
                                pageDecision,
                                cancellationToken);

                            // ═══════════════════════════════════════════════════════════════════
                            // QUALITY GATE: Final validation before sending
                            // ═══════════════════════════════════════════════════════════════════
                            if (!_qualityGate.ValidateBeforeSending(outputBytes, pageDecision, printerProfile))
                            {
                                Debug.WriteLine($"[UniversalRIP] Pre-send Quality Gate failed for page {pageIndex}");
                                throw new InvalidOperationException("Quality Gate validation failed");
                            }

                            // Send to printer (with retry logic)
                            bool sent = false;
                            int retryCount = 0;
                            const int maxRetries = 3;

                            while (!sent && retryCount < maxRetries)
                            {
                                try
                                {
                                    await SendToPrinterAsync(printerName, outputBytes, pageDecision.OutputLanguage,
                                        cancellationToken, System.IO.Path.GetFileName(pdfPath));
                                    sent = true;
                                    result.PagesPrinted++;
                                }
                                catch (Exception sendEx)
                                {
                                    retryCount++;
                                    PrintLogger.Warning(
                                        "[UniversalRIP] Print attempt {Attempt}/{Max} failed for page {Page}. Error: {Error}",
                                        retryCount, maxRetries, pageIndex + 1, sendEx.Message);

                                    if (retryCount >= maxRetries)
                                    {
                                        PrintLogger.Error(sendEx,
                                            "[UniversalRIP] Page {Page} FAILED after {MaxRetries} attempts. Printer: '{Printer}'",
                                            pageIndex + 1, maxRetries, printerName);
                                        result.FailedPages.Add(pageIndex);
                                        result.Errors.Add($"Page {pageIndex + 1}: {sendEx.Message}");
                                        break; // Skip this page, continue with next
                                    }

                                    // PRIORITY: Speed - Minimal retry delay (max 200ms)
                                    await Task.Delay(Math.Min(200 * retryCount, 200), cancellationToken);
                                }
                            }

                            // Handle copies
                            if (sent && copies > 1)
                            {
                                for (int copy = 1; copy < copies; copy++)
                                {
                                    cancellationToken.ThrowIfCancellationRequested();

                                    // Re-render for each copy
                                    var copyResult = await _renderStrategy.RenderPageAsync(
                                        pdfPath,
                                        pageIndex,
                                        ConvertToRenderDecision(pageDecision),
                                        pageProfile,
                                        cancellationToken);

                                    var copyUniversalResult = new RenderResult
                                    {
                                        RasterImage = copyResult.RasterizedImage,
                                        SourcePdfPath = pdfPath,
                                        IsNativeVector = copyResult.RequiresNativeOutput
                                    };

                                    var copyOutput = await _outputStrategy.GenerateSafeOutputAsync(
                                        copyUniversalResult,
                                        pageDecision,
                                        cancellationToken);

                                    await SendToPrinterAsync(printerName, copyOutput, pageDecision.OutputLanguage,
                                        cancellationToken, System.IO.Path.GetFileName(pdfPath));
                                    copyResult.Dispose();
                                }
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;   // the operator stopped the batch
                        }
                        catch (Exception pageEx)
                        {
                            PrintLogger.Error(pageEx,
                                "[UniversalRIP] Page {Page} of {Total} failed on '{Printer}'. Continuing with the next page.",
                                pageIndex + 1, pageCount, printerName);

                            result.FailedPages.Add(pageIndex);
                            result.Errors.Add($"Page {pageIndex + 1}: {pageEx.Message}");
                        }
                        finally
                        {
                            renderResult?.Dispose();
                        }
                    }

                    result.Success = result.FailedPages.Count == 0;

                    if (!result.Success)
                    {
                        result.ErrorMessage =
                            $"طُبعت {result.PagesPrinted} صفحة، وفشلت {result.FailedPages.Count}: " +
                            string.Join(" | ", result.Errors.Take(3));

                        PrintLogger.Warning(
                            "[UniversalRIP] '{Printer}': {Printed} pages printed, {Failed} failed.",
                            printerName, result.PagesPrinted, result.FailedPages.Count);
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    // This used to be a Debug.WriteLine, which writes nothing in a
                    // released build — a field failure left no trace of its cause.
                    PrintLogger.Error(ex,
                        "[UniversalRIP] Pipeline aborted for '{Printer}' after {Printed} pages.",
                        printerName, result.PagesPrinted);

                    result.Success = false;
                    result.ErrorMessage = $"{ex.GetType().Name}: {ex.Message}";
                    return result;
                }
            }, cancellationToken);
        }

        private RenderDecision ConvertToRenderDecision(UniversalRenderDecision universalDecision)
        {
            return new RenderDecision
            {
                Strategy = universalDecision.Strategy,
                RequiredDpi = universalDecision.RequiredDpi,
                PreserveText = universalDecision.PreserveText,
                PreserveVectors = universalDecision.PreserveVectors,
                RequiresRasterization = universalDecision.RequiresRasterization,
                OutputFormat = universalDecision.OutputLanguage,
                QualityLevel = universalDecision.QualityLevel,
                DecisionReason = universalDecision.DecisionReason
            };
        }


        /// <param name="jobName">
        /// The queue entry's name. Every job went in as "Apex Print Job", so a queue of
        /// twenty files showed twenty identical rows — the operator could not tell them
        /// apart, cancel one, or match a jam to the file that caused it.
        /// </param>
        private async Task SendToPrinterAsync(
            string printerName,
            byte[] data,
            PrintLanguage language,
            CancellationToken cancellationToken,
            string? jobName = null)
        {
            await Task.Run(() =>
            {
                try
                {
                    // Use Windows Raw Printing API - now properly throws exceptions with Win32 details
                    SendRawDataToPrinter(printerName, data, language.ToString(), jobName);
                    PrintLogger.Info("[UniversalRIP] Successfully sent {Bytes} bytes to printer '{Printer}' as {Language}",
                        data.Length, printerName, language);
                }
                catch (Win32Exception win32Ex)
                {
                    // Win32 API error - already logged by RawPrinterHelper
                    PrintLogger.Error(win32Ex,
                        "[UniversalRIP] Win32 print API failed. Printer: '{Printer}', Language: {Language}, Error: {Error}",
                        printerName, language, win32Ex.NativeErrorCode);
                    throw;
                }
                catch (Exception ex)
                {
                    PrintLogger.Error(ex,
                        "[UniversalRIP] Print submission failed. Printer: '{Printer}', Language: {Language}, Bytes: {Bytes}",
                        printerName, language, data.Length);
                    throw;
                }
            }, cancellationToken);
        }

        private bool SendRawDataToPrinter(
            string printerName, byte[] data, string dataType, string? jobName = null)
        {
            // FIXED: Use the correct RawPrinterHelper.SendBytesToPrinter method
            // This method handles ALL Win32 API calls correctly with proper error handling

            // Allocate unmanaged memory for the data
            IntPtr pUnmanagedBytes = Marshal.AllocCoTaskMem(data.Length);
            try
            {
                // Copy byte array to unmanaged memory
                Marshal.Copy(data, 0, pUnmanagedBytes, data.Length);

                // Send to printer with correct parameters
                return Apex.Services.Helpers.RawPrinterHelper.SendBytesToPrinter(
                    printerName,
                    pUnmanagedBytes,
                    data.Length,
                    jobName);
            }
            finally
            {
                // Free unmanaged memory
                Marshal.FreeCoTaskMem(pUnmanagedBytes);
            }
        }

        #endregion
    }

    /// <summary>
    /// Result of Universal RIP printing operation.
    /// </summary>
    public class UniversalPrintResult
    {
        public bool Success { get; set; }
        public string PrinterName { get; set; } = "";
        public string FilePath { get; set; } = "";
        public int TotalPages { get; set; }
        public int PagesPrinted { get; set; }
        public List<int> FailedPages { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public string? ErrorMessage { get; set; }

        public UniversalPrinterProfile? PrinterProfile { get; set; }
        public PrintLanguage DetectedLanguage { get; set; }
        public UniversalRenderDecision? Decision { get; set; }
    }
}
