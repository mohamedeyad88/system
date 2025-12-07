using Apex.Core.Enums;
using Apex.Core.Interfaces;
using Apex.Core.Models;
using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Apex.Services
{
    public class PrintJobProcessor : IDisposable
    {
        private readonly Channel<PrintJob> _jobChannel;
        private readonly ILoggerService _logger;
        private readonly IPrinterService _printerService;
        private readonly CancellationTokenSource _cts;
        private readonly Task[] _workerTasks;

        public PrintJobProcessor(ILoggerService logger, IPrinterService printerService, int workerCount = 4)
        {
            _logger = logger;
            _printerService = printerService;
            _jobChannel = Channel.CreateUnbounded<PrintJob>();
            _cts = new CancellationTokenSource();
            
            _workerTasks = new Task[workerCount];
            for (int i = 0; i < workerCount; i++)
            {
                _workerTasks[i] = Task.Run(ProcessJobsAsync);
            }
        }

        public async Task EnqueueJobAsync(PrintJob job)
        {
            await _jobChannel.Writer.WriteAsync(job);
            _logger.Log(LogLevel.Info, $"Job {job.Id} enqueued.", "PrintJobProcessor", "EnqueueJobAsync");
        }

        private async Task ProcessJobsAsync()
        {
            await foreach (var job in _jobChannel.Reader.ReadAllAsync(_cts.Token))
            {
                try
                {
                    _logger.Log(LogLevel.Info, $"Processing Job {job.Id} on Thread {Environment.CurrentManagedThreadId}", "PrintJobProcessor", "ProcessJobsAsync");
                    
                    job.StartedAtUtc = DateTime.UtcNow;
                    job.Status = PrintJobStatus.Processing;

                    // Execute printing
                    bool success = await _printerService.PrintFileAsync(job.PrinterName, job.FilePath, job.TotalCopies);

                    if (success)
                    {
                        job.Status = PrintJobStatus.Completed;
                        job.FinishedAtUtc = DateTime.UtcNow;
                        _logger.Log(LogLevel.Info, $"Job {job.Id} completed successfully.", "PrintJobProcessor", "ProcessJobsAsync");
                    }
                    else
                    {
                        job.Status = PrintJobStatus.Error;
                        job.ErrorMessage = "Failed to send job to printer.";
                        _logger.Log(LogLevel.Error, $"Job {job.Id} failed to print.", "PrintJobProcessor", "ProcessJobsAsync");
                    }
                }
                catch (Exception ex)
                {
                    _logger.Log(LogLevel.Error, $"Error processing job {job.Id}", "PrintJobProcessor", "ProcessJobsAsync", ex);
                    job.Status = PrintJobStatus.Error;
                }
            }
        }

        public void Dispose()
        {
            _jobChannel.Writer.Complete();
            _cts.Cancel();
            try { Task.WaitAll(_workerTasks, 1000); } catch { }
            _cts.Dispose();
        }
    }
}
