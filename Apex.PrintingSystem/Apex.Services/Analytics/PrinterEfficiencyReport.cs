using System;
using System.Collections.Generic;
using System.Linq;
using Apex.Services.Printing.Resilience;

namespace Apex.Services.Analytics
{
    public class PrinterEfficiencyMetrics
    {
        public string   PrinterName              { get; set; } = "";
        public int      TotalJobsToday           { get; set; }
        public int      SuccessfulJobsToday      { get; set; }
        public int      FailedJobsToday          { get; set; }
        public int      TotalPagesToday          { get; set; }
        public double   SuccessRateToday         { get; set; }
        public double   AveragePagesPerMinute    { get; set; }
        public TimeSpan TotalPrintTimeToday      { get; set; }
        public decimal  TotalCostToday           { get; set; }
        public decimal  TotalRevenueToday        { get; set; }
        public int      CircuitBreakerTrips      { get; set; }  // Times circuit opened today
        public int      HealthScore              { get; set; }   // 0–100 composite score
        public string   EfficiencyGrade          { get; set; } = "";  // A/B/C/D/F
        public string   StatusArabic             { get; set; } = "";
    }

    public class PrinterEfficiencyReport
    {
        // ── Singleton ──────────────────────────────────────────────────────────
        private static readonly Lazy<PrinterEfficiencyReport> _instance =
            new(() => new PrinterEfficiencyReport());

        public static PrinterEfficiencyReport Instance => _instance.Value;

        private PrinterEfficiencyReport() { }

        // ── Public API ──────────────────────────────────────────────────────────

        /// <summary>Generate efficiency metrics for a specific printer on a given date (defaults to today).</summary>
        public PrinterEfficiencyMetrics GetPrinterMetrics(string printerName, DateTime? date = null)
        {
            var targetDate = (date ?? DateTime.Today).Date;
            var tracker    = PrintCostTracker.Instance;
            var cbManager  = CircuitBreakerManager.Instance;

            var records = tracker
                .GetRecordsForRange(targetDate, targetDate)
                .Where(r => r.PrinterName == printerName)
                .ToList();

            var metrics = new PrinterEfficiencyMetrics
            {
                PrinterName           = printerName,
                TotalJobsToday        = records.Count,
                SuccessfulJobsToday   = records.Count(r => r.WasSuccessful),
                FailedJobsToday       = records.Count(r => !r.WasSuccessful),
                TotalPagesToday       = records.Sum(r => r.Pages * r.Copies),
                SuccessRateToday      = records.Count > 0
                                            ? records.Count(r => r.WasSuccessful) / (double)records.Count * 100.0
                                            : 0.0,
                AveragePagesPerMinute = records.Any(r => r.PagesPerMinute > 0)
                                            ? records.Where(r => r.PagesPerMinute > 0)
                                                     .Average(r => r.PagesPerMinute)
                                            : 0.0,
                TotalPrintTimeToday   = records.Count > 0
                                            ? TimeSpan.FromTicks(records.Sum(r => r.PrintDuration.Ticks))
                                            : TimeSpan.Zero,
                TotalCostToday        = records.Sum(r => r.TotalCost),
                TotalRevenueToday     = records.Sum(r => r.ChargedPrice),
                CircuitBreakerTrips   = CountCircuitTripsToday(printerName, cbManager, targetDate)
            };

            metrics.HealthScore      = CalculateHealthScore(metrics);
            metrics.EfficiencyGrade  = GetGrade(metrics.HealthScore);
            metrics.StatusArabic     = GetArabicStatus(metrics);

            return metrics;
        }

        /// <summary>Get metrics for all printers that had activity on the given date (defaults to today).</summary>
        public List<PrinterEfficiencyMetrics> GetAllPrinterMetrics(DateTime? date = null)
        {
            var targetDate = (date ?? DateTime.Today).Date;
            var tracker    = PrintCostTracker.Instance;

            var printerNames = tracker
                .GetRecordsForRange(targetDate, targetDate)
                .Select(r => r.PrinterName)
                .Distinct()
                .OrderBy(n => n)
                .ToList();

            return printerNames
                .Select(name => GetPrinterMetrics(name, targetDate))
                .OrderByDescending(m => m.TotalJobsToday)
                .ToList();
        }

        /// <summary>Get top N printers by job count on the given date (defaults to today).</summary>
        public List<PrinterEfficiencyMetrics> GetTopPrinters(int n = 5, DateTime? date = null)
        {
            return GetAllPrinterMetrics(date)
                .Take(n)
                .ToList();
        }

        // ── Private helpers ─────────────────────────────────────────────────────

        /// <summary>
        /// Composite health score 0–100:
        ///   successRateScore  = successRate * 40       (max 40 pts — rate is 0..1 fraction)
        ///   speedScore        = min(avgPPM / 20 * 30, 30)   (max 30 pts, 20 ppm = full)
        ///   reliabilityScore  = trips==0 ? 30 : max(0, 30 - trips*10)
        /// </summary>
        private int CalculateHealthScore(PrinterEfficiencyMetrics metrics)
        {
            double successFraction   = metrics.SuccessRateToday / 100.0; // 0..1
            double successRateScore  = successFraction * 40.0;
            double speedScore        = Math.Min(metrics.AveragePagesPerMinute / 20.0 * 30.0, 30.0);
            double reliabilityScore  = metrics.CircuitBreakerTrips == 0
                                           ? 30.0
                                           : Math.Max(0.0, 30.0 - metrics.CircuitBreakerTrips * 10.0);

            return (int)Math.Round(successRateScore + speedScore + reliabilityScore);
        }

        /// <summary>Grade letter based on health score.</summary>
        private string GetGrade(int score)
        {
            if (score >= 90) return "A";
            if (score >= 80) return "B";
            if (score >= 70) return "C";
            if (score >= 60) return "D";
            return "F";
        }

        /// <summary>Arabic status description derived from metrics.</summary>
        private string GetArabicStatus(PrinterEfficiencyMetrics m)
        {
            // Circuit breaker issues take priority
            if (m.CircuitBreakerTrips > 0)
                return $"تعطل الدائرة {m.CircuitBreakerTrips} مرة — تحقق من الطابعة";

            if (m.TotalJobsToday == 0)
                return "لا توجد وظائف اليوم";

            if (m.HealthScore >= 90)
                return $"أداء ممتاز — معدل نجاح {m.SuccessRateToday:F0}%";

            if (m.HealthScore >= 70)
                return $"أداء جيد — {m.SuccessfulJobsToday} وظيفة ناجحة من {m.TotalJobsToday}";

            if (m.HealthScore >= 50)
                return $"أداء متوسط — {m.FailedJobsToday} فشل من أصل {m.TotalJobsToday} وظيفة";

            return $"أداء ضعيف — {m.FailedJobsToday} فشل، نقاط الصحة: {m.HealthScore}";
        }

        /// <summary>
        /// Count how many times the circuit for this printer opened today.
        /// Uses CircuitBreakerManager to check current state; since we cannot replay
        /// historical trip events, we approximate by checking the Open/HalfOpen state
        /// and correlating with failed job count reported by the tracker.
        /// </summary>
        private static int CountCircuitTripsToday(
            string printerName,
            CircuitBreakerManager cbManager,
            DateTime targetDate)
        {
            // We approximate trips from the number of jobs that failed today relative to
            // the failure threshold.  The circuit breaker fires at 3 consecutive failures
            // (default threshold).  If today's failed count >= threshold, we estimate trips.
            var records = PrintCostTracker.Instance
                .GetRecordsForRange(targetDate, targetDate)
                .Where(r => r.PrinterName == printerName)
                .ToList();

            int failed = records.Count(r => !r.WasSuccessful);

            // Current live state from circuit breaker
            var state = cbManager.GetState(printerName);
            if (state == Printing.Resilience.CircuitState.Open ||
                state == Printing.Resilience.CircuitState.HalfOpen)
            {
                // At least one trip occurred
                return Math.Max(1, failed / 3);
            }

            // Circuit is currently closed — estimate from failure clusters
            return failed >= 3 ? failed / 3 : 0;
        }
    }
}
