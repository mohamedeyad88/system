using Apex.Core.Interfaces;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Management;
using System.Text.Json;
using System.Threading.Tasks;

namespace Apex.Services.Printing
{
    /// <summary>
    /// 🛡️ FAULT TOLERANCE MANAGER
    /// 
    /// Monitors printer and network states.
    /// Manages checkpoints for resumable printing.
    /// Classifies errors and determines recovery strategies.
    /// </summary>
    public class FaultToleranceManager : IFaultToleranceManager
    {
        private readonly ILoggerService _logger;
        private readonly ConcurrentDictionary<Guid, PrintCheckpoint> _checkpoints;
        private readonly string _checkpointFolder;

        public event EventHandler<PrinterIssueEventArgs>? PrinterIssueDetected;
        public event EventHandler<PrinterRecoveryEventArgs>? PrinterRecovered;

        public FaultToleranceManager(ILoggerService logger)
        {
            _logger = logger;
            _checkpoints = new ConcurrentDictionary<Guid, PrintCheckpoint>();
            
            _checkpointFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ApexPrintingSystem", "Checkpoints");
            
            Directory.CreateDirectory(_checkpointFolder);
            
            // Load existing checkpoints
            LoadCheckpointsAsync().Wait();
        }

        public async Task<PrintCheckpoint> CreateCheckpointAsync(Guid jobId, int lastSuccessfulPage)
        {
            var checkpoint = new PrintCheckpoint
            {
                JobId = jobId,
                TicketId = jobId,
                LastSuccessfulPage = lastSuccessfulPage,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                State = CheckpointState.Resumable
            };

            _checkpoints[jobId] = checkpoint;
            
            // Persist to disk
            await SaveCheckpointAsync(checkpoint);
            
            _logger.Log(LogLevel.Info, $"Checkpoint created for job {jobId} at page {lastSuccessfulPage}", 
                "FaultTolerance", "Checkpoint");
            
            return checkpoint;
        }

        public Task<PrintCheckpoint?> GetCheckpointAsync(Guid jobId)
        {
            _checkpoints.TryGetValue(jobId, out var checkpoint);
            return Task.FromResult(checkpoint);
        }

        public async Task ClearCheckpointAsync(Guid jobId)
        {
            _checkpoints.TryRemove(jobId, out _);
            
            var filePath = GetCheckpointFilePath(jobId);
            if (File.Exists(filePath))
            {
                try
                {
                    File.Delete(filePath);
                }
                catch (Exception ex)
                {
                    _logger.Log(LogLevel.Warning, $"Failed to delete checkpoint file: {ex.Message}", 
                        "FaultTolerance", "Clear");
                }
            }
            
            await Task.CompletedTask;
        }

        public async Task<PrinterHealthStatus> CheckPrinterHealthAsync(string printerName)
        {
            var status = new PrinterHealthStatus
            {
                PrinterName = printerName,
                CheckedAt = DateTime.UtcNow
            };

            try
            {
                await Task.Run(() =>
                {
                    // Use WMI to check printer status
                    var query = new SelectQuery($"SELECT * FROM Win32_Printer WHERE Name = '{printerName.Replace("'", "''")}'");
                    
                    using var searcher = new ManagementObjectSearcher(query);
                    var printers = searcher.Get();

                    foreach (ManagementObject printer in printers)
                    {
                        status.IsOnline = true;
                        
                        var printerStatus = printer["PrinterStatus"];
                        var detectedError = printer["DetectedErrorState"];
                        var workOffline = printer["WorkOffline"];
                        
                        status.IsReady = printerStatus != null && (uint)printerStatus == 3; // 3 = Idle/Ready
                        
                        if (workOffline != null && (bool)workOffline)
                        {
                            status.IsOnline = false;
                            status.ErrorType = PrinterErrorType.Offline;
                            status.ErrorMessage = "Printer is offline";
                        }
                        
                        if (detectedError != null)
                        {
                            var errorState = (ushort)detectedError;
                            switch (errorState)
                            {
                                case 0: status.IsReady = true; break;
                                case 1: status.ErrorType = PrinterErrorType.Unknown; break;
                                case 2: status.IsTonerLow = true; break;
                                case 3: status.ErrorType = PrinterErrorType.OutOfToner; break;
                                case 4: status.HasPaperJam = true; status.ErrorType = PrinterErrorType.PaperJam; break;
                                case 5: status.IsPaperLow = true; break;
                                case 6: status.ErrorType = PrinterErrorType.OutOfPaper; break;
                                case 7: status.ErrorType = PrinterErrorType.CoverOpen; break;
                                default: status.ErrorType = PrinterErrorType.Unknown; break;
                            }
                        }
                        
                        // Get queue depth
                        var jobs = printer["Jobs"];
                        if (jobs != null)
                        {
                            status.QueueDepth = Convert.ToInt32(jobs);
                        }
                        
                        break;
                    }

                    if (printers.Count == 0)
                    {
                        status.IsOnline = false;
                        status.ErrorType = PrinterErrorType.Offline;
                        status.ErrorMessage = "Printer not found";
                    }
                });
            }
            catch (Exception ex)
            {
                status.HasError = true;
                status.ErrorType = PrinterErrorType.Unknown;
                status.ErrorMessage = ex.Message;
                
                _logger.Log(LogLevel.Warning, $"Error checking printer health: {ex.Message}", 
                    "FaultTolerance", "Health");
            }

            // Determine if ready
            status.IsReady = status.IsOnline && !status.HasError && status.ErrorType == null;
            
            return status;
        }

        public ErrorClassification ClassifyError(Exception exception, string printerName)
        {
            var classification = new ErrorClassification
            {
                TechnicalDetails = exception.ToString()
            };

            // Classify based on exception type and message
            var message = exception.Message.ToLowerInvariant();

            if (exception is FileNotFoundException)
            {
                classification.Severity = ErrorSeverity.Critical;
                classification.Strategy = RecoveryStrategy.Abort;
                classification.IsRecoverable = false;
                classification.UserFriendlyMessage = "The file could not be found.";
            }
            else if (message.Contains("offline") || message.Contains("not available"))
            {
                classification.Severity = ErrorSeverity.Recoverable;
                classification.Strategy = RecoveryStrategy.Pause;
                classification.IsRecoverable = true;
                classification.ShouldRetry = true;
                classification.RecommendedRetryCount = 5;
                classification.RecommendedRetryDelay = TimeSpan.FromSeconds(30);
                classification.UserFriendlyMessage = "Printer is offline. Will retry when available.";
            }
            else if (message.Contains("paper jam"))
            {
                classification.Severity = ErrorSeverity.Recoverable;
                classification.Strategy = RecoveryStrategy.Pause;
                classification.IsRecoverable = true;
                classification.UserFriendlyMessage = "Paper jam detected. Please clear and printing will resume.";
            }
            else if (message.Contains("out of paper"))
            {
                classification.Severity = ErrorSeverity.Recoverable;
                classification.Strategy = RecoveryStrategy.Pause;
                classification.IsRecoverable = true;
                classification.UserFriendlyMessage = "Out of paper. Please add paper and printing will resume.";
            }
            else if (message.Contains("timeout") || message.Contains("network"))
            {
                classification.Severity = ErrorSeverity.Transient;
                classification.Strategy = RecoveryStrategy.RetryWithDelay;
                classification.IsRecoverable = true;
                classification.ShouldRetry = true;
                classification.RecommendedRetryCount = 3;
                classification.RecommendedRetryDelay = TimeSpan.FromSeconds(5);
                classification.UserFriendlyMessage = "Network issue detected. Retrying...";
            }
            else if (message.Contains("spooler"))
            {
                classification.Severity = ErrorSeverity.Critical;
                classification.Strategy = RecoveryStrategy.ResubmitJob;
                classification.IsRecoverable = true;
                classification.UserFriendlyMessage = "Print spooler error. Job will be resubmitted.";
            }
            else
            {
                classification.Severity = ErrorSeverity.Recoverable;
                classification.Strategy = RecoveryStrategy.RetryWithDelay;
                classification.IsRecoverable = true;
                classification.ShouldRetry = true;
                classification.RecommendedRetryCount = 2;
                classification.RecommendedRetryDelay = TimeSpan.FromSeconds(3);
                classification.UserFriendlyMessage = "An error occurred. Retrying...";
            }

            return classification;
        }

        private async Task SaveCheckpointAsync(PrintCheckpoint checkpoint)
        {
            var filePath = GetCheckpointFilePath(checkpoint.JobId);
            var json = JsonSerializer.Serialize(checkpoint, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, json);
        }

        private async Task LoadCheckpointsAsync()
        {
            try
            {
                var files = Directory.GetFiles(_checkpointFolder, "*.json");
                foreach (var file in files)
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(file);
                        var checkpoint = JsonSerializer.Deserialize<PrintCheckpoint>(json);
                        if (checkpoint != null && checkpoint.State == CheckpointState.Resumable)
                        {
                            _checkpoints[checkpoint.JobId] = checkpoint;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Log(LogLevel.Warning, $"Failed to load checkpoint: {ex.Message}", 
                            "FaultTolerance", "Load");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Warning, $"Failed to load checkpoints: {ex.Message}", 
                    "FaultTolerance", "Load");
            }
        }

        private string GetCheckpointFilePath(Guid jobId)
        {
            return Path.Combine(_checkpointFolder, $"{jobId}.json");
        }

        protected virtual void OnPrinterIssueDetected(PrinterIssueEventArgs e)
        {
            PrinterIssueDetected?.Invoke(this, e);
        }

        protected virtual void OnPrinterRecovered(PrinterRecoveryEventArgs e)
        {
            PrinterRecovered?.Invoke(this, e);
        }
    }
}
