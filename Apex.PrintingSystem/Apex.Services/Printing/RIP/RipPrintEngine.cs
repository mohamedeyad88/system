using Apex.Services.Printing.RIP.Models;
using Apex.Services.Printing.VendorDetection;
using Apex.Services.Helpers;
using Apex.Services.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.Services.Printing.RIP
{
    /// <summary>
    /// RIP Print Engine - Main orchestrator for RIP-like printing.
    /// 
    /// ARCHITECTURE:
    /// 1. Content Analysis → Analyze page content
    /// 2. Decision Engine → Determine rendering strategy
    /// 3. Hybrid Render → Execute rendering
    /// 4. Output Generator → Generate printer-specific output
    /// 5. Send to Printer → Deliver output
    /// </summary>
    public class RipPrintEngine
    {
        private readonly ContentAnalysisEngine _contentAnalyzer;
        private readonly RipDecisionEngine _decisionEngine;
        private readonly HybridRenderStrategy _renderStrategy;
        private readonly PrinterOutputGenerator _outputGenerator;

        public RipPrintEngine()
        {
            _contentAnalyzer = new ContentAnalysisEngine();
            _decisionEngine = new RipDecisionEngine();
            _renderStrategy = new HybridRenderStrategy();
            _outputGenerator = new PrinterOutputGenerator();
        }

        /// <summary>
        /// Print a PDF file using RIP-like processing.
        /// </summary>
        public async Task<bool> PrintPdfAsync(
            string pdfPath,
            string printerName,
            PrinterMetadata printerMetadata,
            int copies = 1,
            QualityLevel qualityLevel = QualityLevel.Professional,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Step 1: Content Analysis (sample first few pages for performance)
                Debug.WriteLine("[RIP] Analyzing content...");
                var representativeProfile = await _contentAnalyzer.AnalyzeSampleAsync(
                    pdfPath,
                    samplePages: 3,
                    cancellationToken);

                // Step 2: Decision Engine
                Debug.WriteLine("[RIP] Making rendering decision...");
                var decision = _decisionEngine.MakeDecision(
                    representativeProfile,
                    printerMetadata,
                    qualityLevel);

                Debug.WriteLine($"[RIP] Strategy: {decision.Strategy}, DPI: {decision.RequiredDpi}, Format: {decision.OutputFormat}");
                Debug.WriteLine($"[RIP] Reason: {decision.DecisionReason}");

                // Step 3: Process and print
                if (decision.Strategy == RenderStrategy.NativeVector &&
                    decision.OutputFormat == PrintLanguage.PDF &&
                    printerMetadata.Capabilities?.SupportsPdf == true)
                {
                    // Direct PDF printing for PDF-native printers
                    return await PrintDirectPdfAsync(pdfPath, printerName, copies, cancellationToken);
                }
                else
                {
                    // Use RIP processing pipeline
                    return await ProcessWithRipPipelineAsync(
                        pdfPath,
                        printerName,
                        printerMetadata,
                        decision,
                        copies,
                        cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RIP] Error: {ex.Message}");
                // Fallback to standard printing
                return await FallbackToStandardPrintAsync(pdfPath, printerName, copies, cancellationToken);
            }
        }

        #region Private Processing Methods

        private async Task<bool> ProcessWithRipPipelineAsync(
            string pdfPath,
            string printerName,
            PrinterMetadata printerMetadata,
            RenderDecision decision,
            int copies,
            CancellationToken cancellationToken)
        {
            return await Task.Run(async () =>
            {
                try
                {
                    using var pdfDocument = PdfiumViewer.PdfDocument.Load(pdfPath);
                    int pageCount = pdfDocument.PageCount;

                    // Process each page
                    for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // Analyze this specific page
                        var pageProfile = await _contentAnalyzer.AnalyzePageAsync(
                            pdfPath,
                            pageIndex,
                            cancellationToken);

                        // Make page-specific decision (may differ from document-level)
                        var pageDecision = _decisionEngine.MakeDecision(
                            pageProfile,
                            printerMetadata,
                            decision.QualityLevel);

                        // Render page
                        var renderResult = await _renderStrategy.RenderPageAsync(
                            pdfPath,
                            pageIndex,
                            pageDecision,
                            pageProfile,
                            cancellationToken);

                        // Generate output
                        var outputBytes = await _outputGenerator.GenerateOutputAsync(
                            renderResult,
                            pageDecision,
                            pageProfile,
                            cancellationToken);

                        // Send to printer
                        if (outputBytes.Length > 0)
                        {
                            await SendToPrinterAsync(printerName, outputBytes, cancellationToken,
                                System.IO.Path.GetFileName(pdfPath));
                        }

                        // Cleanup
                        renderResult.Dispose();

                        // Handle copies
                        if (copies > 1)
                        {
                            for (int copy = 1; copy < copies; copy++)
                            {
                                // Re-render and send for each copy
                                var copyResult = await _renderStrategy.RenderPageAsync(
                                    pdfPath,
                                    pageIndex,
                                    pageDecision,
                                    pageProfile,
                                    cancellationToken);

                                var copyOutput = await _outputGenerator.GenerateOutputAsync(
                                    copyResult,
                                    pageDecision,
                                    pageProfile,
                                    cancellationToken);

                                if (copyOutput.Length > 0)
                                {
                                    await SendToPrinterAsync(printerName, copyOutput, cancellationToken,
                                        System.IO.Path.GetFileName(pdfPath));
                                }

                                copyResult.Dispose();
                            }
                        }
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[RIP] Pipeline error: {ex.Message}");
                    return false;
                }
            }, cancellationToken);
        }

        private async Task<bool> PrintDirectPdfAsync(
            string pdfPath,
            string printerName,
            int copies,
            CancellationToken cancellationToken)
        {
            // For PDF-native printers, send PDF directly
            return await Task.Run(() =>
            {
                try
                {
                    var pdfBytes = File.ReadAllBytes(pdfPath);
                    return SendRawDataToPrinter(printerName, pdfBytes, "RAW");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[RIP] Direct PDF print failed: {ex.Message}");
                    return false;
                }
            }, cancellationToken);
        }

        private async Task<bool> FallbackToStandardPrintAsync(
            string pdfPath,
            string printerName,
            int copies,
            CancellationToken cancellationToken)
        {
            // Fallback to standard PdfDirectPrinter
            try
            {
                var pdfPrinter = new PdfDirectPrinter();
                return await pdfPrinter.PrintPdfAsync(printerName, pdfPath, copies);
            }
            catch
            {
                return false;
            }
        }

        /// <param name="jobName">
        /// The document's file name, shown in the printer queue. Without it every job
        /// appears as "Apex Print Job" and the operator cannot tell one from another.
        /// </param>
        private async Task SendToPrinterAsync(
            string printerName,
            byte[] data,
            CancellationToken cancellationToken,
            string? jobName = null)
        {
            await Task.Run(() =>
            {
                try
                {
                    // RawPrinterHelper now throws Win32Exception with detailed error messages
                    SendRawDataToPrinter(printerName, data, "RAW", jobName);
                    PrintLogger.Info("[RIP] Successfully sent {Bytes} bytes to printer '{Printer}'",
                        data.Length, printerName);
                }
                catch (Win32Exception win32Ex)
                {
                    PrintLogger.Error(win32Ex,
                        "[RIP] Win32 print API failed. Printer: '{Printer}', Error Code: {ErrorCode}",
                        printerName, win32Ex.NativeErrorCode);
                    throw;
                }
                catch (Exception ex)
                {
                    PrintLogger.Error(ex,
                        "[RIP] Print submission failed. Printer: '{Printer}', Bytes: {Bytes}",
                        printerName, data.Length);
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
            IntPtr pUnmanagedBytes = System.Runtime.InteropServices.Marshal.AllocCoTaskMem(data.Length);
            try
            {
                // Copy byte array to unmanaged memory
                System.Runtime.InteropServices.Marshal.Copy(data, 0, pUnmanagedBytes, data.Length);

                // Send to printer with correct parameters
                return RawPrinterHelper.SendBytesToPrinter(
                    printerName,
                    pUnmanagedBytes,
                    data.Length,
                    jobName);
            }
            finally
            {
                // Free unmanaged memory
                System.Runtime.InteropServices.Marshal.FreeCoTaskMem(pUnmanagedBytes);
            }
        }

        #endregion
    }
}
