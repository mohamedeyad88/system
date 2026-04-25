using Apex.Services.Analytics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Apex.UI.ViewModels
{
    public partial class AnalyticsDashboardViewModel : ViewModelBase
    {
        // ── Timer ──────────────────────────────────────────────────────────────
        private readonly DispatcherTimer _refreshTimer;

        // ── Revenue section ────────────────────────────────────────────────────
        [ObservableProperty] private decimal _todayRevenue;
        [ObservableProperty] private decimal _todayCost;
        [ObservableProperty] private decimal _todayProfit;
        [ObservableProperty] private double  _todayMarginPercent;
        [ObservableProperty] private string  _todayRevenueText  = "0.00 ج.م";
        [ObservableProperty] private string  _profitText        = "0.00 ج.م";
        [ObservableProperty] private string  _marginText        = "0.0%";

        // ── Jobs section ───────────────────────────────────────────────────────
        [ObservableProperty] private int     _todayTotalJobs;
        [ObservableProperty] private int     _todaySuccessJobs;
        [ObservableProperty] private int     _todayFailedJobs;
        [ObservableProperty] private double  _todaySuccessRate;
        [ObservableProperty] private string  _successRateText   = "0%";

        // ── Pages ──────────────────────────────────────────────────────────────
        [ObservableProperty] private int     _todayTotalPages;
        [ObservableProperty] private decimal _revenuePerPage;
        [ObservableProperty] private string  _revenuePerPageText = "0.00 ج.م";

        // ── Weekly ──────────────────────────────────────────────────────────────
        [ObservableProperty] private decimal _weekRevenue;
        [ObservableProperty] private decimal _weekProfit;
        [ObservableProperty] private string  _weekSummaryText   = "";
        [ObservableProperty] private double  _weekChangePercent;
        [ObservableProperty] private string  _weekChangeText    = "";

        // ── Month estimate ──────────────────────────────────────────────────────
        [ObservableProperty] private decimal _estimatedMonthRevenue;
        [ObservableProperty] private string  _estimatedMonthText = "";

        // ── Last refresh ───────────────────────────────────────────────────────
        [ObservableProperty] private string  _lastRefreshTime   = "--:--:--";

        // ── Collections ────────────────────────────────────────────────────────
        [ObservableProperty] private ObservableCollection<PrinterEfficiencyMetrics> _printerMetrics = new();
        [ObservableProperty] private ObservableCollection<RevenueDataPoint>         _weeklyTrend    = new();
        [ObservableProperty] private ObservableCollection<DailyCostSummary>         _last7Days      = new();

        // ── Constructor ─────────────────────────────────────────────────────────
        public AnalyticsDashboardViewModel()
        {
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(60)
            };
            _refreshTimer.Tick += async (_, _) => await RefreshAsync();
            _refreshTimer.Start();
        }

        // ── Async init (called by framework / view) ─────────────────────────────
        public override async Task InitializeAsync()
        {
            await RefreshAsync();
        }

        // ── Commands ────────────────────────────────────────────────────────────

        [RelayCommand]
        public async Task RefreshAsync()
        {
            try
            {
                // ── Today aggregates ───────────────────────────────────────────
                var todayReport = RevenueAnalytics.Instance.GetTodayReport();
                TodayRevenue      = todayReport.TotalRevenue;
                TodayCost         = todayReport.TotalCost;
                TodayProfit       = todayReport.TotalProfit;
                TodayMarginPercent = todayReport.AvgMarginPercent;
                TodayTotalJobs    = todayReport.TotalJobs;
                TodayTotalPages   = todayReport.TotalPages;
                RevenuePerPage    = todayReport.RevenuePerPage;

                // Counts from raw records
                var todayRecords  = PrintCostTracker.Instance.GetTodayRecords();
                TodaySuccessJobs  = todayRecords.Count(r => r.WasSuccessful);
                TodayFailedJobs   = todayRecords.Count(r => !r.WasSuccessful);
                TodaySuccessRate  = TodayTotalJobs > 0
                                        ? TodaySuccessJobs / (double)TodayTotalJobs * 100.0
                                        : 0.0;

                // Formatted strings
                TodayRevenueText   = $"{TodayRevenue:F2} ج.م";
                ProfitText         = $"{TodayProfit:F2} ج.م";
                MarginText         = $"{TodayMarginPercent:F1}%";
                SuccessRateText    = $"{TodaySuccessRate:F1}%";
                RevenuePerPageText = $"{RevenuePerPage:F2} ج.م";

                // ── Week report ────────────────────────────────────────────────
                var weekReport   = RevenueAnalytics.Instance.GetWeekReport();
                WeekRevenue      = weekReport.TotalRevenue;
                WeekProfit       = weekReport.TotalProfit;
                WeekSummaryText  = weekReport.SummaryArabic;

                // ── Week-over-week ──────────────────────────────────────────────
                var (thisWeek, lastWeek, changePct) = RevenueAnalytics.Instance.GetWeekOverWeekChange();
                WeekChangePercent = changePct;
                if (changePct >= 0)
                    WeekChangeText = $"▲ {changePct:F1}% مقارنة بالأسبوع الماضي";
                else
                    WeekChangeText = $"▼ {Math.Abs(changePct):F1}% مقارنة بالأسبوع الماضي";

                // ── Month estimate ──────────────────────────────────────────────
                EstimatedMonthRevenue = RevenueAnalytics.Instance.EstimateMonthEndRevenue();
                EstimatedMonthText    = $"تقدير نهاية الشهر: {EstimatedMonthRevenue:F2} ج.م";

                // ── Printer metrics ─────────────────────────────────────────────
                var metrics = PrinterEfficiencyReport.Instance.GetAllPrinterMetrics();
                PrinterMetrics.Clear();
                foreach (var m in metrics)
                    PrinterMetrics.Add(m);

                // ── Weekly trend ────────────────────────────────────────────────
                var trend = RevenueAnalytics.Instance.GetWeeklyTrend();
                WeeklyTrend.Clear();
                foreach (var dp in trend)
                    WeeklyTrend.Add(dp);

                // ── Last 7 days summaries ───────────────────────────────────────
                var last7 = PrintCostTracker.Instance.GetLastNDays(7);
                Last7Days.Clear();
                foreach (var s in last7)
                    Last7Days.Add(s);

                LastRefreshTime = DateTime.Now.ToString("HH:mm:ss");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AnalyticsDashboard] RefreshAsync error: {ex.Message}");
            }
        }

        [RelayCommand]
        public void ExportCsvReport()
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Title            = "حفظ تقرير CSV",
                    Filter           = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                    DefaultExt       = ".csv",
                    FileName         = $"ApexReport_{DateTime.Today:yyyy-MM-dd}.csv"
                };

                if (dialog.ShowDialog() != true)
                    return;

                var records = PrintCostTracker.Instance.GetTodayRecords();

                // Build CSV with UTF-8 BOM for Excel Arabic compatibility
                var sb = new StringBuilder();

                // Header row
                sb.AppendLine(
                    "رقم الوظيفة,اسم الوظيفة,الطابعة,وقت البدء,وقت الانتهاء," +
                    "الصفحات,النسخ,ألوان,وجهان,ناجحة," +
                    "تكلفة الحبر,تكلفة الورق,تكلفة الجهاز,إجمالي التكلفة," +
                    "السعر المحاسَب,الربح,المدة (ثانية),صفحات/دقيقة");

                foreach (var r in records)
                {
                    sb.AppendLine(string.Join(",",
                        EscapeCsv(r.JobId),
                        EscapeCsv(r.JobName),
                        EscapeCsv(r.PrinterName),
                        r.StartedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                        r.CompletedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                        r.Pages,
                        r.Copies,
                        r.IsColor        ? "نعم" : "لا",
                        r.IsDoubleSided  ? "نعم" : "لا",
                        r.WasSuccessful  ? "نعم" : "لا",
                        r.InkCost.ToString("F4"),
                        r.PaperCost.ToString("F4"),
                        r.MachineCost.ToString("F4"),
                        r.TotalCost.ToString("F4"),
                        r.ChargedPrice.ToString("F4"),
                        r.Profit.ToString("F4"),
                        ((int)r.PrintDuration.TotalSeconds).ToString(),
                        r.PagesPerMinute.ToString("F2")
                    ));
                }

                // Write with UTF-8 BOM
                File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

                // Open the file so the user can review it immediately
                Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AnalyticsDashboard] ExportCsvReport error: {ex.Message}");
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private static string EscapeCsv(string value)
        {
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }
    }
}
