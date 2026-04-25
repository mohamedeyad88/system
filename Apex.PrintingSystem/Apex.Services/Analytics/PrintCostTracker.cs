using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Apex.Services.Printing.Queue;

namespace Apex.Services.Analytics
{
    public class PrintJobCostRecord
    {
        public string   JobId            { get; set; } = "";
        public string   JobName          { get; set; } = "";
        public string   PrinterName      { get; set; } = "";
        public DateTime StartedAt        { get; set; }
        public DateTime CompletedAt      { get; set; }
        public int      Pages            { get; set; }
        public int      Copies           { get; set; }
        public bool     IsColor          { get; set; }
        public bool     IsDoubleSided    { get; set; }
        public bool     WasSuccessful    { get; set; }

        // Cost breakdown (EGP)
        public decimal  InkCost          { get; set; }
        public decimal  PaperCost        { get; set; }
        public decimal  MachineCost      { get; set; }   // Depreciation per page
        public decimal  TotalCost        { get; set; }
        public decimal  ChargedPrice     { get; set; }   // What was billed to customer
        public decimal  Profit           { get; set; }   // ChargedPrice - TotalCost

        public TimeSpan PrintDuration    { get; set; }
        public double   PagesPerMinute   { get; set; }
    }

    public class DailyCostSummary
    {
        public DateTime Date              { get; set; }
        public int      TotalJobs         { get; set; }
        public int      SuccessfulJobs    { get; set; }
        public int      FailedJobs        { get; set; }
        public int      TotalPages        { get; set; }
        public decimal  TotalCost         { get; set; }
        public decimal  TotalRevenue      { get; set; }
        public decimal  TotalProfit       { get; set; }
        public double   SuccessRate       { get; set; }
        public Dictionary<string, int>     JobsPerPrinter  { get; set; } = new();
        public Dictionary<string, decimal> CostPerPrinter  { get; set; } = new();
    }

    public class CostCalculationConfig
    {
        public decimal  InkCostPerPageBW     { get; set; } = 0.05m;   // EGP
        public decimal  InkCostPerPageColor  { get; set; } = 0.25m;   // EGP
        public decimal  PaperCostPerSheetA4  { get; set; } = 0.10m;   // EGP
        public decimal  MachineCostPerPage   { get; set; } = 0.02m;   // EGP depreciation
        public decimal  DefaultMarkupPercent { get; set; } = 30m;     // Profit margin
    }

    public class PrintCostTracker
    {
        // ── Singleton ──────────────────────────────────────────────────────────
        private static readonly Lazy<PrintCostTracker> _instance =
            new(() => new PrintCostTracker());

        public static PrintCostTracker Instance => _instance.Value;

        // ── State ──────────────────────────────────────────────────────────────
        private readonly ConcurrentBag<PrintJobCostRecord> _records = new();

        public CostCalculationConfig Config { get; set; } = new();

        // ── Events ──────────────────────────────────────────────────────────────
        public event EventHandler<PrintJobCostRecord>? JobRecorded;

        // ── Constructor ─────────────────────────────────────────────────────────
        private PrintCostTracker()
        {
            Debug.WriteLine("[PrintCostTracker] Initialized");
        }

        // ── Public API ──────────────────────────────────────────────────────────

        /// <summary>Store a completed job record and fire the JobRecorded event.</summary>
        public void RecordJob(PrintJobCostRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            _records.Add(record);
            Debug.WriteLine($"[PrintCostTracker] Recorded job {record.JobId} — cost {record.TotalCost:F2} EGP, charged {record.ChargedPrice:F2} EGP");
            JobRecorded?.Invoke(this, record);
        }

        /// <summary>
        /// Calculate cost from raw job parameters and return a fully populated record.
        /// Does NOT automatically store the record — call RecordJob() afterwards if desired.
        /// </summary>
        public PrintJobCostRecord CalculateJobCost(
            string jobId, string jobName, string printerName,
            int pages, int copies, bool isColor, bool isDoubleSided,
            DateTime startedAt, DateTime completedAt, bool wasSuccessful)
        {
            var duration = completedAt - startedAt;

            // Effective pages to bill (double-sided reduces paper/ink consumption)
            decimal effectivePages = isDoubleSided
                ? (decimal)Math.Ceiling(pages / 2.0)
                : pages;

            decimal inkCost     = effectivePages * copies * (isColor ? Config.InkCostPerPageColor : Config.InkCostPerPageBW);
            decimal paperCost   = effectivePages * copies * Config.PaperCostPerSheetA4;
            decimal machineCost = pages          * copies * Config.MachineCostPerPage;
            decimal totalCost   = inkCost + paperCost + machineCost;
            decimal charged     = totalCost * (1 + Config.DefaultMarkupPercent / 100m);
            decimal profit      = charged - totalCost;
            double  ppm         = duration.TotalMinutes > 0 ? pages / duration.TotalMinutes : 0;

            return new PrintJobCostRecord
            {
                JobId          = jobId,
                JobName        = jobName,
                PrinterName    = printerName,
                StartedAt      = startedAt,
                CompletedAt    = completedAt,
                Pages          = pages,
                Copies         = copies,
                IsColor        = isColor,
                IsDoubleSided  = isDoubleSided,
                WasSuccessful  = wasSuccessful,
                InkCost        = inkCost,
                PaperCost      = paperCost,
                MachineCost    = machineCost,
                TotalCost      = totalCost,
                ChargedPrice   = charged,
                Profit         = profit,
                PrintDuration  = duration,
                PagesPerMinute = ppm
            };
        }

        /// <summary>Get all records for today (local date).</summary>
        public IReadOnlyList<PrintJobCostRecord> GetTodayRecords()
        {
            var today = DateTime.Today;
            return _records
                .Where(r => r.StartedAt.Date == today)
                .OrderByDescending(r => r.StartedAt)
                .ToList();
        }

        /// <summary>Get records where StartedAt falls within [from.Date, to.Date] inclusive.</summary>
        public IReadOnlyList<PrintJobCostRecord> GetRecordsForRange(DateTime from, DateTime to)
        {
            var fromDate = from.Date;
            var toDate   = to.Date;
            return _records
                .Where(r => r.StartedAt.Date >= fromDate && r.StartedAt.Date <= toDate)
                .OrderBy(r => r.StartedAt)
                .ToList();
        }

        /// <summary>Build a DailyCostSummary for a specific calendar date.</summary>
        public DailyCostSummary GetDailySummary(DateTime date)
        {
            var day     = date.Date;
            var records = _records.Where(r => r.StartedAt.Date == day).ToList();

            var summary = new DailyCostSummary
            {
                Date           = day,
                TotalJobs      = records.Count,
                SuccessfulJobs = records.Count(r => r.WasSuccessful),
                FailedJobs     = records.Count(r => !r.WasSuccessful),
                TotalPages     = records.Sum(r => r.Pages * r.Copies),
                TotalCost      = records.Sum(r => r.TotalCost),
                TotalRevenue   = records.Sum(r => r.ChargedPrice),
                TotalProfit    = records.Sum(r => r.Profit),
                SuccessRate    = records.Count > 0
                                     ? records.Count(r => r.WasSuccessful) / (double)records.Count * 100.0
                                     : 0.0
            };

            // Per-printer breakdowns
            foreach (var grp in records.GroupBy(r => r.PrinterName))
            {
                summary.JobsPerPrinter[grp.Key]  = grp.Count();
                summary.CostPerPrinter[grp.Key]  = grp.Sum(r => r.TotalCost);
            }

            return summary;
        }

        /// <summary>Get daily summaries for the last N calendar days (including today).</summary>
        public List<DailyCostSummary> GetLastNDays(int days)
        {
            var result = new List<DailyCostSummary>(days);
            for (int i = days - 1; i >= 0; i--)
                result.Add(GetDailySummary(DateTime.Today.AddDays(-i)));
            return result;
        }

        /// <summary>Aggregate totals across all stored records.</summary>
        public (int TotalJobs, decimal TotalCost, decimal TotalRevenue, decimal TotalProfit) GetSessionTotals()
        {
            var all = _records.ToList();
            return (
                all.Count,
                all.Sum(r => r.TotalCost),
                all.Sum(r => r.ChargedPrice),
                all.Sum(r => r.Profit)
            );
        }

        /// <summary>Return aggregated per-printer stats for a date range.</summary>
        public Dictionary<string, (int Jobs, decimal Cost, decimal Revenue)> GetPerPrinterStats(
            DateTime from, DateTime to)
        {
            var records = GetRecordsForRange(from, to);
            return records
                .GroupBy(r => r.PrinterName)
                .ToDictionary(
                    g => g.Key,
                    g => (g.Count(), g.Sum(r => r.TotalCost), g.Sum(r => r.ChargedPrice))
                );
        }
    }
}
