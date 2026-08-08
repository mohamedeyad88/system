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

        private async Task SendJobToPrinter(PrintJob job)
        {
            try
            {
                // ═══════════════════════════════════════════════════════════════════
                // VENDOR-AWARE PRINT GATEWAY (MANDATORY)
                // All print jobs MUST pass through this gateway for:
                // - Silent vendor detection (HP, Epson, etc.)
                // - Automatic profile optimization
                // - Error handling with vendor-specific recovery
                // ═══════════════════════════════════════════════════════════════════

                var gateway = Printing.VendorDetection.VendorAwarePrintGateway.Instance;

                var result = await gateway.PrintAsync(
                    job.PrinterName,
                    job.FilePath,
                    job.TotalCopies,
                    job);

                if (!result.Success)
                {
                    System.Diagnostics.Debug.WriteLine($"[VendorGateway] Print failed: {result.ErrorMessage}");
                    System.Diagnostics.Debug.WriteLine($"[VendorGateway] Vendor: {result.Vendor}, Profile: {result.ProfileUsed}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[VendorGateway] Print succeeded in {result.ElapsedMs}ms");
                    System.Diagnostics.Debug.WriteLine($"[VendorGateway] Vendor: {result.Vendor}, Profile: {result.ProfileUsed}");
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SendJobToPrinter failed: {ex.Message}");
            }
        }

        private async Task PrintImageAsync(string printerName, string filePath)
        {
            await Task.Run(() =>
            {
                using var image = System.Drawing.Image.FromFile(filePath);
                using var pd = new System.Drawing.Printing.PrintDocument();
                pd.PrinterSettings.PrinterName = printerName;
                pd.PrintPage += (s, e) =>
                {
                    if (e.Graphics != null)
                        e.Graphics.DrawImage(image, e.MarginBounds);
                };
                pd.Print();
            });
        }

        private async Task PrintTextFileAsync(string printerName, string filePath)
        {
            await Task.Run(() =>
            {
                try
                {
                    var text = System.IO.File.ReadAllText(filePath);
                    using var pd = new System.Drawing.Printing.PrintDocument();
                    pd.PrinterSettings.PrinterName = printerName;
                    var lines = text.Split('\n');
                    int idx = 0;
                    pd.PrintPage += (s, e) =>
                    {
                        if (e.Graphics == null) return;
                        using var font = new System.Drawing.Font("Arial", 10);
                        float y = e.MarginBounds.Top;
                        while (idx < lines.Length && y < e.MarginBounds.Bottom)
                        {
                            e.Graphics.DrawString(lines[idx++], font, System.Drawing.Brushes.Black, e.MarginBounds.Left, y);
                            y += font.GetHeight(e.Graphics);
                        }
                        e.HasMorePages = idx < lines.Length;
                    };
                    pd.Print();
                }
                catch { }
            });
        }
    }
}
