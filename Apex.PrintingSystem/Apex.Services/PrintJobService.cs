using Apex.Core.Enums;
using Apex.Core.Interfaces;
using Apex.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apex.Services
{
    public class PrintJobService : IPrintJobService
    {
        private readonly IRepository<PrintJob> _printJobRepository;

        public PrintJobService(IRepository<PrintJob> printJobRepository)
        {
            _printJobRepository = printJobRepository;
        }

        public async Task<IEnumerable<PrintJob>> GetAllPrintJobsAsync()
        {
            return await _printJobRepository.GetAllAsync();
        }

        public async Task<IEnumerable<PrintJob>> GetPendingPrintJobsAsync()
        {
            return await _printJobRepository.FindAsync(j => j.Status == PrintJobStatus.Pending);
        }

        public async Task<PrintJob?> GetPrintJobByIdAsync(int id)
        {
            return await _printJobRepository.GetByIdAsync(id);
        }

        public async Task CreatePrintJobAsync(PrintJob printJob)
        {
            await _printJobRepository.AddAsync(printJob);
        }

        public async Task UpdatePrintJobAsync(PrintJob printJob)
        {
            await _printJobRepository.UpdateAsync(printJob);
        }

        public async Task DeletePrintJobAsync(int id)
        {
            var job = await _printJobRepository.GetByIdAsync(id);
            if (job != null)
            {
                await _printJobRepository.DeleteAsync(job);
            }
        }

        public async Task CreateBatchPrintJobs(IEnumerable<string> filePaths, IEnumerable<string> printerNames)
        {
            foreach (var printerName in printerNames)
            {
                foreach (var filePath in filePaths)
                {
                    var job = new PrintJob
                    {
                        FilePath = filePath,
                        FileName = System.IO.Path.GetFileName(filePath),
                        PrinterName = printerName,
                        Status = Core.Enums.PrintJobStatus.Pending,
                        CreatedAt = System.DateTime.Now
                    };
                    await _printJobRepository.AddAsync(job);
                }
            }
        }

        public async Task ProcessPendingJobs()
        {
            // Note: In a real app, this would be more complex (concurrency, etc.)
            var pendingJobs = await _printJobRepository.FindAsync(j => j.Status == Core.Enums.PrintJobStatus.Pending);
            foreach (var job in pendingJobs)
            {
                try
                {
                    job.Status = Core.Enums.PrintJobStatus.Printing;
                    job.StartedAtUtc = System.DateTime.UtcNow;
                    await _printJobRepository.UpdateAsync(job);

                    await SendJobToPrinter(job);

                    job.Status = Core.Enums.PrintJobStatus.Completed;
                    job.FinishedAtUtc = System.DateTime.UtcNow;
                    await _printJobRepository.UpdateAsync(job);
                }
                catch (System.Exception ex)
                {
                    job.Status = Core.Enums.PrintJobStatus.Error;
                    job.ErrorMessage = ex.Message;
                    await _printJobRepository.UpdateAsync(job);
                }
            }
        }

        private Task SendJobToPrinter(PrintJob job)
        {
            return Task.Run(() =>
            {
                var info = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = job.FilePath,
                    Verb = "printto",
                    Arguments = $"\"{job.PrinterName}\"",
                    CreateNoWindow = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(info);
            });
        }
    }
}
