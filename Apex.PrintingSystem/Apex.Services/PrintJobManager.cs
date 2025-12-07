using Apex.Core.Interfaces;
using Apex.Core.Models;
using Apex.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.Services
{
    public class PrintJobManager : IPrintJobManager
    {
        private readonly ApexDbContext _context;
        private readonly IRoutingRulesEngine _rulesEngine;
        private readonly ILoadBalancer _loadBalancer;
        private readonly IPrinterDiscoveryService _discoveryService;

        public PrintJobManager(
            ApexDbContext context,
            IRoutingRulesEngine rulesEngine,
            ILoadBalancer loadBalancer,
            IPrinterDiscoveryService discoveryService)
        {
            _context = context;
            _rulesEngine = rulesEngine;
            _loadBalancer = loadBalancer;
            _discoveryService = discoveryService;
        }

        public async Task<Guid> CreateJobsAsync(IEnumerable<string> filePaths, JobCreateOptions options)
        {
            var batchId = Guid.NewGuid();

            foreach (var filePath in filePaths)
            {
                // 1. Create initial job object
                var job = new PrintJob
                {
                    JobGuid = Guid.NewGuid().ToString(),
                    FilePath = filePath,
                    OriginalFileName = Path.GetFileName(filePath),
                    Mode = options.MergeAsOne ? "MergedTask" : "SeparateTask",
                    Status = Core.Enums.PrintJobStatus.Pending,
                    OptionsJson = JsonSerializer.Serialize(options.GlobalOptions),
                    CreatedAtUtc = DateTime.UtcNow,
                    TargetPoolName = options.TargetPool
                };

                // 2. Apply Routing Rules (if no specific printer/pool set)
                if (string.IsNullOrEmpty(options.TargetPool) && !options.TargetPrinters.Any())
                {
                    var rule = await _rulesEngine.EvaluateRulesAsync(job);
                    if (rule != null)
                    {
                        // Apply rule actions (simplified)
                        // In real impl, parse ActionsJson and apply
                        // e.g. job.TargetPoolName = rule.Actions["assignToPool"];
                    }
                }

                // 3. Assign Printer (if pool is set or auto-assigned)
                if (!string.IsNullOrEmpty(job.TargetPoolName))
                {
                    // Logic to find printers in pool would go here
                    // For now, let's just use LoadBalancer on all printers as a fallback or mock pool
                    var allPrinters = await _discoveryService.ScanAsync();
                    // Filter by pool if we had pool info in PrinterInfo
                    var bestPrinter = _loadBalancer.SelectPrinterForJob(job, allPrinters);
                    if (bestPrinter != null)
                    {
                        job.TargetPrinterName = bestPrinter.Name;
                    }
                }
                else if (options.TargetPrinters.Any())
                {
                    // If multiple target printers, we might create multiple jobs or just pick one
                    // Spec says "batch printing to multiple printers", so maybe duplicate job?
                    // For this method, let's assume one job per file, assigned to first target or load balanced among targets
                    job.TargetPrinterName = options.TargetPrinters.First();
                }

                _context.PrintJobs.Add(job);
            }

            await _context.SaveChangesAsync();
            return batchId;
        }

        public async Task StartProcessingAsync(CancellationToken ct)
        {
            // Background loop to pick Queued jobs and process them
            // This would be called by a background service
            await Task.CompletedTask;
        }

        public async Task RetryJobAsync(Guid jobGuid)
        {
            var job = await _context.PrintJobs.FirstOrDefaultAsync(j => j.JobGuid == jobGuid.ToString());
            if (job != null)
            {
                job.Status = Core.Enums.PrintJobStatus.Pending;
                job.Attempts++;
                job.ErrorMessage = null;
                await _context.SaveChangesAsync();
            }
        }
    }
}
