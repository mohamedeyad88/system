using Apex.Core.Interfaces;
using Apex.Core.Models;
using Apex.Services.Printing.VendorDetection;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.Services.Printing
{
    /// <summary>
    /// SMART PRINT MANAGER - THE MANDATORY ENTRY POINT FOR ALL PRINTING
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// 
    /// 🚨 CRITICAL ARCHITECTURAL RULE:
    /// Every single print command in the entire application MUST pass through this manager.
    /// 
    /// ❌ FORBIDDEN:
    ///    - Direct calls to OS print APIs
    ///    - Direct driver calls
    ///    - Shell "printto" commands
    ///    - Any bypass of this manager
    /// 
    /// ✅ REQUIRED:
    ///    - All print commands use SmartPrintManager.SubmitAsync()
    ///    - Background streaming processing
    ///    - Vendor-aware optimization (HP, Epson, etc.)
    ///    - Fault-tolerant execution
    /// 
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public class SmartPrintManager : ISmartPrintManager
    {
        private static readonly Lazy<SmartPrintManager> _instance = 
            new(() => new SmartPrintManager());
        
        /// <summary>
        /// Singleton instance - use this for all printing operations.
        /// </summary>
        public static SmartPrintManager Instance => _instance.Value;
        
        private readonly VendorAwarePrintGateway _vendorGateway;
        private readonly object _lock = new();
        private int _activeJobs = 0;
        private int _totalJobsSubmitted = 0;
        private int _totalJobsCompleted = 0;
        private int _totalJobsFailed = 0;
        
        // ═══════════════════════════════════════════════════════════════════
        // CRITICAL: Printer locks to prevent concurrent jobs on same printer
        // ═══════════════════════════════════════════════════════════════════
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _printerLocks = new();
        private const int MAX_CONCURRENT_JOBS_PER_PRINTER = 1; // ONE job at a time per printer
        
        /// <summary>
        /// Status message for UI display (non-technical).
        /// </summary>
        public event Action<string>? StatusChanged;
        
        /// <summary>
        /// Progress update for current job (0-100).
        /// </summary>
        public event Action<int>? ProgressChanged;
        
        /// <summary>
        /// Fires when a job completes (success or failure).
        /// </summary>
        public event Action<SmartPrintResult>? JobCompleted;
        
        private SmartPrintManager()
        {
            _vendorGateway = VendorAwarePrintGateway.Instance;
            _vendorGateway.StatusChanged += s => StatusChanged?.Invoke(s);
            _vendorGateway.ProgressChanged += p => ProgressChanged?.Invoke(p);

            // Start WMI spooler watcher for real page progress
            PrintSpoolerWatcher.Instance.Start();
        }
        
        #region Public API
        
        /// <summary>
        /// Submit a print job to the Smart Printing Engine.
        /// This is the ONLY method that should be used for printing across the entire application.
        /// </summary>
        /// <param name="request">Print request with all parameters.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>Result of the print operation.</returns>
        public async Task<SmartPrintResult> SubmitAsync(
            SmartPrintRequest request,
            CancellationToken cancellationToken = default)
        {
            // Validation
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            
            if (string.IsNullOrEmpty(request.FilePath))
                throw new ArgumentException("File path is required", nameof(request));
            
            if (!File.Exists(request.FilePath))
                throw new FileNotFoundException("File not found", request.FilePath);
            
            if (request.PrinterNames == null || request.PrinterNames.Count == 0)
                throw new ArgumentException("At least one printer must be specified", nameof(request));
            
            var result = new SmartPrintResult
            {
                RequestId = Guid.NewGuid(),
                FilePath = request.FilePath,
                Copies = request.Copies,
                StartTime = DateTime.UtcNow
            };
            
            Interlocked.Increment(ref _totalJobsSubmitted);
            Interlocked.Increment(ref _activeJobs);
            
            // ═══════════════════════════════════════════════════════════════════
            // CRITICAL LOGGING: Track every print job to detect infinite loop
            // ═══════════════════════════════════════════════════════════════════
            Apex.Services.Logging.PrintLogger.Warning(
                "========== NEW PRINT JOB STARTED ==========\n" +
                "JobId: {JobId}\n" +
                "File: {File}\n" +
                "Printers: {Printers}\n" +
                "Copies: {Copies}\n" +
                "Total Jobs Submitted: {TotalSubmitted}",
                result.RequestId, request.FilePath, string.Join(", ", request.PrinterNames), 
                request.Copies, _totalJobsSubmitted);
            
            try
            {
                UpdateStatus("جاري تجهيز الطباعة...");
                Debug.WriteLine($"[SmartPrintManager] Job submitted: {request.FilePath} to {request.PrinterNames.Count} printers");
                
                // Process each printer
                var printerResults = new List<PrinterJobResult>();
                
                if (request.PrinterNames.Count == 1)
                {
                    // Single printer - direct execution
                    var printerResult = await PrintToSinglePrinterAsync(
                        request.FilePath, 
                        request.PrinterNames[0], 
                        request.Copies,
                        request.Settings,
                        cancellationToken);
                    
                    printerResults.Add(printerResult);
                }
                else
                {
                    // Multiple printers - parallel execution
                    UpdateStatus($"جاري الطباعة إلى {request.PrinterNames.Count} طابعات...");
                    
                    var tasks = request.PrinterNames.Select(printerName =>
                        PrintToSinglePrinterAsync(
                            request.FilePath, 
                            printerName, 
                            request.Copies,
                            request.Settings,
                            cancellationToken));
                    
                    var results = await Task.WhenAll(tasks);
                    printerResults.AddRange(results);
                }
                
                // Compile results
                result.PrinterResults = printerResults;
                result.SuccessCount = printerResults.Count(r => r.Success);
                result.FailureCount = printerResults.Count(r => !r.Success);
                result.Success = result.FailureCount == 0;
                result.EndTime = DateTime.UtcNow;
                
                if (result.Success)
                {
                    Interlocked.Increment(ref _totalJobsCompleted);
                    UpdateStatus("اكتملت الطباعة ✓");
                }
                else if (result.SuccessCount > 0)
                {
                    Interlocked.Increment(ref _totalJobsCompleted);
                    UpdateStatus($"اكتملت الطباعة جزئياً ({result.SuccessCount}/{printerResults.Count})");
                }
                else
                {
                    Interlocked.Increment(ref _totalJobsFailed);
                    UpdateStatus("فشلت الطباعة");
                    result.ErrorMessage = string.Join("; ", printerResults.Where(r => !r.Success).Select(r => r.ErrorMessage));
                }
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.ErrorMessage = "تم إلغاء الطباعة";
                result.WasCancelled = true;
                UpdateStatus("تم إلغاء الطباعة");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SmartPrintManager] Error: {ex}");
                result.Success = false;
                result.ErrorMessage = GetFriendlyError(ex);
                Interlocked.Increment(ref _totalJobsFailed);
                UpdateStatus("حدث خطأ أثناء الطباعة");
            }
            finally
            {
                Interlocked.Decrement(ref _activeJobs);
                result.EndTime = DateTime.UtcNow;
                
                // ═══════════════════════════════════════════════════════════════════
                // CRITICAL LOGGING: Track job completion
                // ═══════════════════════════════════════════════════════════════════
                Apex.Services.Logging.PrintLogger.Warning(
                    "========== PRINT JOB ENDED ==========\n" +
                    "JobId: {JobId}\n" +
                    "Success: {Success}\n" +
                    "Duration: {Duration}ms\n" +
                    "Total Completed: {TotalCompleted}\n" +
                    "Total Failed: {TotalFailed}",
                    result.RequestId, result.Success, result.Duration.TotalMilliseconds,
                    _totalJobsCompleted, _totalJobsFailed);
                
                JobCompleted?.Invoke(result);
            }
            
            return result;
        }
        
        /// <summary>
        /// Simple overload for single printer, single file.
        /// </summary>
        public async Task<SmartPrintResult> SubmitAsync(
            string filePath,
            string printerName,
            int copies = 1,
            CancellationToken cancellationToken = default)
        {
            return await SubmitAsync(new SmartPrintRequest
            {
                FilePath = filePath,
                PrinterNames = new List<string> { printerName },
                Copies = copies
            }, cancellationToken);
        }
        
        /// <summary>
        /// Submit to multiple printers at once.
        /// </summary>
        public async Task<SmartPrintResult> SubmitToMultipleAsync(
            string filePath,
            IEnumerable<string> printerNames,
            int copies = 1,
            CancellationToken cancellationToken = default)
        {
            return await SubmitAsync(new SmartPrintRequest
            {
                FilePath = filePath,
                PrinterNames = printerNames.ToList(),
                Copies = copies
            }, cancellationToken);
        }
        
        #endregion
        
        #region Statistics
        
        public int ActiveJobs => _activeJobs;
        public int TotalJobsSubmitted => _totalJobsSubmitted;
        public int TotalJobsCompleted => _totalJobsCompleted;
        public int TotalJobsFailed => _totalJobsFailed;
        
        public string GetStatusSummary()
        {
            // Accurate status summary with real-time data
            if (_activeJobs > 0)
            {
                return $"🖨️ جاري الطباعة... {_activeJobs} مهمة نشطة";
            }
            
            var successRate = _totalJobsSubmitted > 0 
                ? (_totalJobsCompleted * 100.0 / _totalJobsSubmitted).ToString("F1")
                : "0";
            
            return $"🖨️ جاهز • تم: {_totalJobsCompleted} | فشل: {_totalJobsFailed} | معدل النجاح: {successRate}%";
        }
        
        #endregion
        
        #region Private Methods
        
        private async Task<PrinterJobResult> PrintToSinglePrinterAsync(
            string filePath,
            string printerName,
            int copies,
            PrintJobSettings? settings,
            CancellationToken cancellationToken)
        {
            var result = new PrinterJobResult
            {
                PrinterName = printerName,
                StartTime = DateTime.UtcNow
            };
            
            var stopwatch = Stopwatch.StartNew();
            
            // ═══════════════════════════════════════════════════════════════════
            // CRITICAL: Acquire printer lock to enforce ONE job per printer
            // ═══════════════════════════════════════════════════════════════════
            var printerLock = _printerLocks.GetOrAdd(printerName, _ => new SemaphoreSlim(MAX_CONCURRENT_JOBS_PER_PRINTER, MAX_CONCURRENT_JOBS_PER_PRINTER));
            
            bool lockAcquired = false;

            // ── WMI spooler handlers defined OUTSIDE try so finally can unsubscribe ──
            void OnSpoolerProgress(object? s, SpoolerJobProgress p)
            {
                if (string.Equals(p.PrinterName, printerName, StringComparison.OrdinalIgnoreCase)
                    && p.TotalPages > 0)
                {
                    ProgressChanged?.Invoke(p.PercentComplete);
                    StatusChanged?.Invoke($"طباعة صفحة {p.PagesPrinted} من {p.TotalPages}");
                }
            }
            void OnSpoolerCompleted(object? s, SpoolerJobProgress p)
            {
                if (string.Equals(p.PrinterName, printerName, StringComparison.OrdinalIgnoreCase))
                {
                    ProgressChanged?.Invoke(100);
                    StatusChanged?.Invoke("اكتملت الطباعة");
                }
            }

            try
            {
                // Try to acquire lock (wait max 30 seconds)
                lockAcquired = await printerLock.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);

                if (!lockAcquired)
                {
                    result.Success = false;
                    result.ErrorMessage = $"Printer '{printerName}' is busy. Too many concurrent jobs.";
                    Apex.Services.Logging.PrintLogger.Warning(
                        "[SmartPrintManager] Could not acquire lock for printer '{Printer}'. Timeout after 30s.",
                        printerName);
                    return result;
                }

                Apex.Services.Logging.PrintLogger.Info(
                    "[SmartPrintManager] Printer lock acquired for '{Printer}'. Starting print job.",
                    printerName);

                Debug.WriteLine($"[SmartPrintManager] Printing to {printerName}...");

                // Create a PrintJob for settings if provided
                PrintJob? jobSettings = null;
                if (settings != null)
                {
                    jobSettings = new PrintJob
                    {
                        TotalCopies = copies,
                        Duplex = settings.Duplex,
                        Color = settings.Color,
                        PageRange = settings.PageRange,
                        Orientation = settings.Orientation
                    };
                }

                // Subscribe to real WMI progress for this printer
                PrintSpoolerWatcher.Instance.JobProgress += OnSpoolerProgress;
                PrintSpoolerWatcher.Instance.JobCompleted += OnSpoolerCompleted;

                // Use vendor-aware gateway
                var gatewayResult = await _vendorGateway.PrintAsync(
                    printerName,
                    filePath,
                    copies,
                    jobSettings,
                    cancellationToken);

                result.Success = gatewayResult.Success;
                result.ErrorMessage = gatewayResult.ErrorMessage;
                result.Vendor = gatewayResult.Vendor;
                result.ProfileUsed = gatewayResult.ProfileUsed;
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.ErrorMessage = "Print job cancelled";
                Apex.Services.Logging.PrintLogger.Info(
                    "[SmartPrintManager] Print job cancelled for printer '{Printer}'",
                    printerName);
                throw; // Re-throw to propagate cancellation
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SmartPrintManager] Error printing to {printerName}: {ex.Message}");
                result.Success = false;
                result.ErrorMessage = GetFriendlyError(ex);
                Apex.Services.Logging.PrintLogger.Error(ex,
                    "[SmartPrintManager] Print job failed for printer '{Printer}'",
                    printerName);
            }
            finally
            {
                // Unsubscribe WMI spooler event handlers
                PrintSpoolerWatcher.Instance.JobProgress -= OnSpoolerProgress;
                PrintSpoolerWatcher.Instance.JobCompleted -= OnSpoolerCompleted;

                // Release printer lock
                if (lockAcquired)
                {
                    printerLock.Release();
                    Apex.Services.Logging.PrintLogger.Info(
                        "[SmartPrintManager] Printer lock released for '{Printer}'",
                        printerName);
                }
                
                stopwatch.Stop();
                result.ElapsedMs = stopwatch.ElapsedMilliseconds;
                result.EndTime = DateTime.UtcNow;
            }
            
            return result;
        }
        
        private void UpdateStatus(string status)
        {
            StatusChanged?.Invoke(status);
        }
        
        private string GetFriendlyError(Exception ex)
        {
            var msg = ex.Message.ToLowerInvariant();
            
            if (msg.Contains("offline"))
                return "الطابعة غير متصلة";
            if (msg.Contains("paper"))
                return "تحقق من الورق";
            if (msg.Contains("network") || msg.Contains("connection"))
                return "مشكلة في الاتصال";
            if (msg.Contains("access"))
                return "لا توجد صلاحية";
            
            return "حدث خطأ أثناء الطباعة";
        }
        
        #endregion
    }
    
    #region Models
    
    /// <summary>
    /// Interface for SmartPrintManager for DI.
    /// </summary>
    public interface ISmartPrintManager
    {
        Task<SmartPrintResult> SubmitAsync(SmartPrintRequest request, CancellationToken cancellationToken = default);
        Task<SmartPrintResult> SubmitAsync(string filePath, string printerName, int copies = 1, CancellationToken cancellationToken = default);
        Task<SmartPrintResult> SubmitToMultipleAsync(string filePath, IEnumerable<string> printerNames, int copies = 1, CancellationToken cancellationToken = default);
        
        event Action<string>? StatusChanged;
        event Action<int>? ProgressChanged;
        event Action<SmartPrintResult>? JobCompleted;
        
        int ActiveJobs { get; }
        string GetStatusSummary();
    }
    
    /// <summary>
    /// Request to submit to Smart Print Manager.
    /// </summary>
    public class SmartPrintRequest
    {
        public string FilePath { get; set; } = string.Empty;
        public List<string> PrinterNames { get; set; } = new();
        public int Copies { get; set; } = 1;
        public PrintJobSettings? Settings { get; set; }
    }
    
    /// <summary>
    /// Result of a smart print operation.
    /// </summary>
    public class SmartPrintResult
    {
        public Guid RequestId { get; set; }
        public bool Success { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public int Copies { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public string? ErrorMessage { get; set; }
        public bool WasCancelled { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public List<PrinterJobResult> PrinterResults { get; set; } = new();
        
        public TimeSpan Duration => EndTime - StartTime;
    }
    
    /// <summary>
    /// Result for individual printer in a batch.
    /// </summary>
    public class PrinterJobResult
    {
        public string PrinterName { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public PrinterVendor Vendor { get; set; }
        public string ProfileUsed { get; set; } = string.Empty;
        public long ElapsedMs { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
    }
    
    #endregion
}
