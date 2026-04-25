using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Apex.Services.Printing.Queue;

namespace Apex.Services.Printing.Queue.Examples
{
    /// <summary>
    /// Real-world usage examples for the Print Queue System.
    /// Copy these patterns into your application code.
    /// </summary>
    public static class UsageExamples
    {
        /// <summary>
        /// Example 1: Simple single job submission.
        /// </summary>
        public static async Task Example1_SingleJob()
        {
            var queueManager = PrintJobQueueManager.Instance;
            queueManager.Start(); // Start once at app startup
            
            // Submit job - returns immediately with job ID
            var jobId = await queueManager.SubmitPrintJobAsync(
                printerName: "HP LaserJet Pro MFP",
                filePath: @"C:\Documents\Invoice_12345.pdf",
                copies: 2
            );
            
            Console.WriteLine($"Job submitted: {jobId}");
            // Job is now queued and will print automatically
        }
        
        /// <summary>
        /// Example 2: Print All - CORRECT pattern for multiple jobs.
        /// This is how "Print All" should be implemented.
        /// </summary>
        public static async Task Example2_PrintAll_Correct()
        {
            var queueManager = PrintJobQueueManager.Instance;
            queueManager.Start();
            
            // Simulate user selecting multiple files
            var files = new[]
            {
                @"C:\Documents\File1.pdf",
                @"C:\Documents\File2.pdf",
                @"C:\Documents\File3.pdf",
                @"C:\Documents\File4.pdf",
                @"C:\Documents\File5.pdf",
                // ... even 100 files!
            };
            
            // ✅ CORRECT: Submit all jobs instantly
            var jobIds = new List<string>();
            foreach (var file in files)
            {
                var jobId = await queueManager.SubmitPrintJobAsync(
                    "Main Printer", 
                    file, 
                    copies: 1
                );
                jobIds.Add(jobId);
            }
            
            Console.WriteLine($"✅ Submitted {jobIds.Count} jobs in milliseconds!");
            Console.WriteLine("System will process them safely with throttling.");
            
            // UI remains responsive!
            // No network congestion!
            // Printers won't freeze!
        }
        
        /// <summary>
        /// Example 3: Monitor job progress with events.
        /// </summary>
        public static async Task Example3_MonitorProgress()
        {
            var queueManager = PrintJobQueueManager.Instance;
            queueManager.Start();
            
            // Subscribe to events
            queueManager.JobStateChanged += (sender, job) =>
            {
                Console.WriteLine($"[{job.JobId}] State: {job.State} - {job.StatusMessage}");
                
                if (job.State == PrintJobState.Completed)
                {
                    Console.WriteLine($"✅ Job completed in {job.ElapsedTime?.TotalSeconds:F1}s");
                }
                else if (job.State == PrintJobState.Failed)
                {
                    Console.WriteLine($"❌ Job failed: {job.ErrorMessage}");
                }
            };
            
            queueManager.JobProgressChanged += (sender, job) =>
            {
                Console.WriteLine($"[{job.JobId}] Progress: {job.Progress}%");
            };
            
            // Submit job
            var jobId = await queueManager.SubmitPrintJobAsync(
                "Office Printer",
                @"C:\Reports\Monthly_Report.pdf",
                copies: 1
            );
            
            // Events will fire automatically as job progresses
        }
        
        /// <summary>
        /// Example 4: Distribute jobs across multiple printers.
        /// </summary>
        public static async Task Example4_MultiPrinterDistribution()
        {
            var queueManager = PrintJobQueueManager.Instance;
            queueManager.Start();
            
            var printers = new[] { "Printer1", "Printer2", "Printer3" };
            var files = new[] { "doc1.pdf", "doc2.pdf", "doc3.pdf", "doc4.pdf", "doc5.pdf" };
            
            // Round-robin distribution
            for (int i = 0; i < files.Length; i++)
            {
                var printer = printers[i % printers.Length];
                await queueManager.SubmitPrintJobAsync(printer, files[i], 1);
            }
            
            // All 3 printers will process jobs in parallel (safely!)
            // Each printer's queue is independent
        }
        
        /// <summary>
        /// Example 5: High-priority urgent job.
        /// </summary>
        public static async Task Example5_UrgentJob()
        {
            var queueManager = PrintJobQueueManager.Instance;
            
            // Create urgent job with high priority
            var urgentJob = new PrintJob
            {
                PrinterName = "Main Printer",
                FilePath = @"C:\Urgent\Critical_Document.pdf",
                Copies = 1,
                Priority = 10, // Higher = more important (default is 5)
                JobName = "URGENT: Critical Document"
            };
            
            var jobId = await queueManager.SubmitPrintJobAsync(
                urgentJob.PrinterName,
                urgentJob.FilePath,
                urgentJob
            );
            
            Console.WriteLine("Urgent job queued with high priority");
        }
        
        /// <summary>
        /// Example 6: Get queue statistics and status.
        /// </summary>
        public static void Example6_QueueStatistics()
        {
            var queueManager = PrintJobQueueManager.Instance;
            
            // Get overall statistics
            var stats = queueManager.GetStatistics();
            Console.WriteLine($"Total Submitted: {stats.TotalJobsQueued}");
            Console.WriteLine($"Completed: {stats.TotalJobsCompleted}");
            Console.WriteLine($"Failed: {stats.TotalJobsFailed}");
            Console.WriteLine($"Success Rate: {stats.SuccessRate:F1}%");
            Console.WriteLine($"Active Jobs: {stats.ActiveJobs}");
            Console.WriteLine($"Queued Jobs: {stats.QueuedJobs}");
            
            // Check specific printer
            var queueDepth = queueManager.GetQueueDepth("Main Printer");
            Console.WriteLine($"Main Printer queue depth: {queueDepth}");
            
            var activeJob = queueManager.GetActiveJob("Main Printer");
            if (activeJob != null)
            {
                Console.WriteLine($"Main Printer currently printing: {activeJob.JobName}");
            }
        }
        
        /// <summary>
        /// Example 7: Handle failed jobs with manual retry.
        /// </summary>
        public static async Task Example7_HandleFailures()
        {
            var queueManager = PrintJobQueueManager.Instance;
            queueManager.Start();
            
            // Subscribe to failures
            queueManager.JobStateChanged += (sender, job) =>
            {
                if (job.State == PrintJobState.Failed)
                {
                    Console.WriteLine($"Job {job.JobId} failed: {job.ErrorMessage}");
                    
                    if (job.CanRetry)
                    {
                        Console.WriteLine("Automatic retry will occur...");
                    }
                    else
                    {
                        Console.WriteLine("Max retries reached. Manual intervention needed.");
                        
                        // Optionally notify user
                        // Show error dialog, send email, log to monitoring system, etc.
                    }
                }
            };
            
            var jobId = await queueManager.SubmitPrintJobAsync(
                "Problematic Printer",
                @"C:\Documents\test.pdf",
                copies: 1,
                maxRetries: 3 // Will retry up to 3 times automatically
            );
        }
        
        /// <summary>
        /// Example 8: Cancel jobs programmatically.
        /// </summary>
        public static async Task Example8_CancelJobs()
        {
            var queueManager = PrintJobQueueManager.Instance;
            queueManager.Start();
            
            // Submit some jobs
            var jobId1 = await queueManager.SubmitPrintJobAsync("Printer1", "file1.pdf", 1);
            var jobId2 = await queueManager.SubmitPrintJobAsync("Printer1", "file2.pdf", 1);
            var jobId3 = await queueManager.SubmitPrintJobAsync("Printer1", "file3.pdf", 1);
            
            // Cancel specific job (only works if not yet started)
            bool cancelled = queueManager.CancelJob(jobId2);
            if (cancelled)
            {
                Console.WriteLine($"Job {jobId2} cancelled successfully");
            }
            else
            {
                Console.WriteLine($"Job {jobId2} cannot be cancelled (already processing)");
            }
        }
        
        /// <summary>
        /// Example 9: Production scenario - Batch invoice printing.
        /// </summary>
        public static async Task Example9_Production_BatchInvoices()
        {
            var queueManager = PrintJobQueueManager.Instance;
            queueManager.Start();
            
            // Simulate generating 50 invoices
            var invoices = new List<string>();
            for (int i = 1; i <= 50; i++)
            {
                invoices.Add($@"C:\Invoices\Invoice_{i:D5}.pdf");
            }
            
            Console.WriteLine($"Processing {invoices.Count} invoices...");
            
            // Track progress
            int completed = 0;
            queueManager.JobStateChanged += (sender, job) =>
            {
                if (job.State == PrintJobState.Completed)
                {
                    completed++;
                    Console.WriteLine($"✅ Progress: {completed}/{invoices.Count} completed");
                }
            };
            
            // Submit all (instant!)
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var invoice in invoices)
            {
                await queueManager.SubmitPrintJobAsync("Invoice Printer", invoice, 1);
            }
            sw.Stop();
            
            Console.WriteLine($"✅ All {invoices.Count} invoices submitted in {sw.ElapsedMilliseconds}ms");
            Console.WriteLine("System will print them safely with throttling.");
            Console.WriteLine("Network will remain stable.");
            Console.WriteLine("Printer will not freeze.");
        }
        
        /// <summary>
        /// Example 10: Graceful shutdown.
        /// </summary>
        public static async Task Example10_GracefulShutdown()
        {
            var queueManager = PrintJobQueueManager.Instance;
            
            // When app is closing...
            Console.WriteLine("Shutting down print queue system...");
            
            // Wait for active jobs to complete
            await queueManager.StopAsync();
            
            Console.WriteLine("Print queue system stopped gracefully.");
            Console.WriteLine("All active jobs completed.");
        }
    }
}
