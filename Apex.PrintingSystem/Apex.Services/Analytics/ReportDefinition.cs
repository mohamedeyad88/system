using System;
using System.Collections.Generic;

namespace Apex.Services.Analytics
{
    public enum ReportType
    {
        DailySummary,   // Single day overview
        WeeklySummary,  // 7-day summary with daily breakdown
        MonthlySummary, // Month with weekly breakdown
        PrinterHealth,  // Per-printer efficiency report
        CostAnalysis,   // Detailed cost/revenue breakdown
        JobHistory,     // Full job listing for a period
        QuotaUsage      // Per-user quota consumption
    }

    public enum ReportFormat { PDF, CSV, Json }

    public enum ReportSchedule { None, Daily, Weekly, Monthly }

    public class ReportDefinition
    {
        public string         Id            { get; set; } = Guid.NewGuid().ToString("N")[..10];
        public string         Name          { get; set; } = "";
        public ReportType     Type          { get; set; }
        public ReportFormat   Format        { get; set; } = ReportFormat.PDF;
        public ReportSchedule Schedule      { get; set; } = ReportSchedule.None;
        public DateTime?      NextRunAt     { get; set; }
        public DateTime?      LastRunAt     { get; set; }
        public string         OutputFolder  { get; set; } = "";
        public bool           IsEnabled     { get; set; } = true;
        public string         CreatedBy     { get; set; } = "";
        public DateTime       CreatedAt     { get; set; } = DateTime.Now;

        // Report parameters
        public DateTime?      FromDate      { get; set; }
        public DateTime?      ToDate        { get; set; }
        public string?        PrinterFilter { get; set; }  // null = all printers
        public string?        UserFilter    { get; set; }  // null = all users
        public bool           IncludeCharts { get; set; } = true;
        public string         CompanyName   { get; set; } = "أبكس لحلول الطباعة المتكاملة";
    }

    public class ReportResult
    {
        public string         ReportId      { get; set; } = "";
        public string         ReportName    { get; set; } = "";
        public ReportType     Type          { get; set; }
        public bool           Success       { get; set; }
        public string?        FilePath      { get; set; }
        public byte[]?        Content       { get; set; }
        public string?        ErrorMessage  { get; set; }
        public DateTime       GeneratedAt   { get; set; } = DateTime.Now;
        public long           FileSizeBytes { get; set; }
        public int            PageCount     { get; set; }
        public string         SummaryArabic { get; set; } = "";
    }
}
