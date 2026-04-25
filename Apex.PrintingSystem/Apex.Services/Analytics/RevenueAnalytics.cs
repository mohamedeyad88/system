using System;
using System.Collections.Generic;
using System.Linq;

namespace Apex.Services.Analytics
{
    public class RevenueDataPoint
    {
        public DateTime Date          { get; set; }
        public decimal  Revenue       { get; set; }
        public decimal  Cost          { get; set; }
        public decimal  Profit        { get; set; }
        public double   MarginPercent { get; set; }
        public int      JobCount      { get; set; }
        public int      PageCount     { get; set; }
    }

    public class RevenueReport
    {
        public DateTime FromDate         { get; set; }
        public DateTime ToDate           { get; set; }
        public decimal  TotalRevenue     { get; set; }
        public decimal  TotalCost        { get; set; }
        public decimal  TotalProfit      { get; set; }
        public double   AvgMarginPercent { get; set; }
        public int      TotalJobs        { get; set; }
        public int      TotalPages       { get; set; }
        public decimal  RevenuePerJob    { get; set; }
        public decimal  RevenuePerPage   { get; set; }
        public decimal  BestDayRevenue   { get; set; }
        public DateTime BestDay          { get; set; }
        public List<RevenueDataPoint> DailyBreakdown { get; set; } = new();
        public string   SummaryArabic    { get; set; } = "";
    }

    public class RevenueAnalytics
    {
        // ── Singleton ──────────────────────────────────────────────────────────
        private static readonly Lazy<RevenueAnalytics> _instance =
            new(() => new RevenueAnalytics());

        public static RevenueAnalytics Instance => _instance.Value;

        private RevenueAnalytics() { }

        // ── Public API ──────────────────────────────────────────────────────────

        /// <summary>Build a RevenueReport for an arbitrary date range (inclusive).</summary>
        public RevenueReport GetReport(DateTime from, DateTime to)
        {
            var tracker   = PrintCostTracker.Instance;
            var fromDate  = from.Date;
            var toDate    = to.Date;
            var records   = tracker.GetRecordsForRange(fromDate, toDate);

            // Build daily data points
            var daily = new List<RevenueDataPoint>();
            for (var d = fromDate; d <= toDate; d = d.AddDays(1))
            {
                var dayRecords = records.Where(r => r.StartedAt.Date == d).ToList();
                decimal rev    = dayRecords.Sum(r => r.ChargedPrice);
                decimal cost   = dayRecords.Sum(r => r.TotalCost);
                decimal profit = dayRecords.Sum(r => r.Profit);
                double  margin = rev > 0 ? (double)(profit / rev) * 100.0 : 0.0;

                daily.Add(new RevenueDataPoint
                {
                    Date          = d,
                    Revenue       = rev,
                    Cost          = cost,
                    Profit        = profit,
                    MarginPercent = margin,
                    JobCount      = dayRecords.Count,
                    PageCount     = dayRecords.Sum(r => r.Pages * r.Copies)
                });
            }

            decimal totalRevenue = daily.Sum(d => d.Revenue);
            decimal totalCost    = daily.Sum(d => d.Cost);
            decimal totalProfit  = daily.Sum(d => d.Profit);
            int     totalJobs    = daily.Sum(d => d.JobCount);
            int     totalPages   = daily.Sum(d => d.PageCount);

            var bestDay = daily.OrderByDescending(d => d.Revenue).FirstOrDefault();

            var report = new RevenueReport
            {
                FromDate         = fromDate,
                ToDate           = toDate,
                TotalRevenue     = totalRevenue,
                TotalCost        = totalCost,
                TotalProfit      = totalProfit,
                AvgMarginPercent = totalRevenue > 0
                                       ? (double)(totalProfit / totalRevenue) * 100.0
                                       : 0.0,
                TotalJobs        = totalJobs,
                TotalPages       = totalPages,
                RevenuePerJob    = totalJobs  > 0 ? totalRevenue / totalJobs  : 0m,
                RevenuePerPage   = totalPages > 0 ? totalRevenue / totalPages : 0m,
                BestDayRevenue   = bestDay?.Revenue ?? 0m,
                BestDay          = bestDay?.Date ?? fromDate,
                DailyBreakdown   = daily
            };

            report.SummaryArabic = BuildArabicSummary(report);
            return report;
        }

        /// <summary>Report covering today only.</summary>
        public RevenueReport GetTodayReport()
            => GetReport(DateTime.Today, DateTime.Today);

        /// <summary>Report covering the current calendar week (Mon–today, or Sun–today depending on locale).</summary>
        public RevenueReport GetWeekReport()
        {
            var today = DateTime.Today;
            // Start on Monday
            int daysFromMonday = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            var weekStart      = today.AddDays(-daysFromMonday);
            return GetReport(weekStart, today);
        }

        /// <summary>Report covering the first day of the current month through today.</summary>
        public RevenueReport GetMonthReport()
        {
            var today      = DateTime.Today;
            var monthStart = new DateTime(today.Year, today.Month, 1);
            return GetReport(monthStart, today);
        }

        /// <summary>Daily data points for the last 7 calendar days (oldest first).</summary>
        public List<RevenueDataPoint> GetWeeklyTrend()
        {
            var to   = DateTime.Today;
            var from = to.AddDays(-6);
            return GetReport(from, to).DailyBreakdown;
        }

        /// <summary>Daily data points for the last 30 calendar days (oldest first).</summary>
        public List<RevenueDataPoint> GetMonthlyTrend()
        {
            var to   = DateTime.Today;
            var from = to.AddDays(-29);
            return GetReport(from, to).DailyBreakdown;
        }

        /// <summary>
        /// Estimate end-of-month revenue by extrapolating the current average daily pace.
        /// Average = TotalRevenueSoFar / DaysElapsedSoFar * DaysInMonth
        /// </summary>
        public decimal EstimateMonthEndRevenue()
        {
            var today      = DateTime.Today;
            var monthStart = new DateTime(today.Year, today.Month, 1);
            int daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);

            var report       = GetReport(monthStart, today);
            int daysElapsed  = (today - monthStart).Days + 1;  // at least 1

            if (daysElapsed <= 0 || report.TotalRevenue == 0m)
                return 0m;

            decimal avgDailyRevenue = report.TotalRevenue / daysElapsed;
            return Math.Round(avgDailyRevenue * daysInMonth, 2);
        }

        /// <summary>
        /// Compare revenue for this week vs the same-length period last week.
        /// Returns (ThisWeek, LastWeek, ChangePercent).
        /// </summary>
        public (decimal ThisWeek, decimal LastWeek, double ChangePercent) GetWeekOverWeekChange()
        {
            var today         = DateTime.Today;
            // Current week: last 7 days
            var thisWeekFrom  = today.AddDays(-6);
            var thisWeekTo    = today;
            // Last week: the 7 days before that
            var lastWeekFrom  = today.AddDays(-13);
            var lastWeekTo    = today.AddDays(-7);

            decimal thisWeek  = GetReport(thisWeekFrom, thisWeekTo).TotalRevenue;
            decimal lastWeek  = GetReport(lastWeekFrom, lastWeekTo).TotalRevenue;

            double changePercent = lastWeek > 0
                ? (double)((thisWeek - lastWeek) / lastWeek) * 100.0
                : (thisWeek > 0 ? 100.0 : 0.0);

            return (thisWeek, lastWeek, changePercent);
        }

        // ── Private helpers ─────────────────────────────────────────────────────

        private string BuildArabicSummary(RevenueReport report)
        {
            double margin = report.AvgMarginPercent;
            return $"إجمالي الإيراد: {report.TotalRevenue:F2} ج.م | الربح: {report.TotalProfit:F2} ج.م ({margin:F1}%) | {report.TotalJobs} وظيفة";
        }
    }
}
