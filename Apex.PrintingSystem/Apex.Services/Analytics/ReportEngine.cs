using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Apex.Services.Analytics
{
    public class ReportEngine
    {
        // ── Singleton ──────────────────────────────────────────────────────────
        private static readonly Lazy<ReportEngine> _instance =
            new(() => new ReportEngine());

        public static ReportEngine Instance => _instance.Value;

        static ReportEngine()
        {
            QuestPDF.Settings.License = LicenseType.Community;
        }

        private ReportEngine() { }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>Generate a report and return the result (bytes in Content).</summary>
        public ReportResult Generate(ReportDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));

            try
            {
                byte[] content;

                if (definition.Format == ReportFormat.CSV || definition.Type == ReportType.JobHistory)
                {
                    content = GenerateJobHistoryCsv(definition);
                }
                else
                {
                    content = definition.Type switch
                    {
                        ReportType.DailySummary   => GenerateDailySummaryPdf(definition),
                        ReportType.WeeklySummary  => GenerateWeeklySummaryPdf(definition),
                        ReportType.MonthlySummary => GenerateWeeklySummaryPdf(definition), // reuse weekly with wider range
                        ReportType.PrinterHealth  => GeneratePrinterHealthPdf(definition),
                        ReportType.CostAnalysis   => GenerateCostAnalysisPdf(definition),
                        ReportType.JobHistory     => GenerateJobHistoryCsv(definition),
                        _                         => GenerateDailySummaryPdf(definition)
                    };
                }

                return new ReportResult
                {
                    ReportId      = definition.Id,
                    ReportName    = definition.Name,
                    Type          = definition.Type,
                    Success       = true,
                    Content       = content,
                    FileSizeBytes = content.Length,
                    PageCount     = 1,
                    SummaryArabic = $"تم إنشاء التقرير: {definition.Name}"
                };
            }
            catch (Exception ex)
            {
                return new ReportResult
                {
                    ReportId     = definition.Id,
                    ReportName   = definition.Name,
                    Type         = definition.Type,
                    Success      = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>Generate and save report to file. Returns result with FilePath set.</summary>
        public ReportResult GenerateToFile(ReportDefinition definition, string? outputPath = null)
        {
            var result = Generate(definition);
            if (!result.Success || result.Content == null)
                return result;

            try
            {
                string folder = outputPath ?? definition.OutputFolder;
                if (string.IsNullOrWhiteSpace(folder))
                    folder = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "Apex", "Reports", "Output");

                Directory.CreateDirectory(folder);

                string ext      = (definition.Format == ReportFormat.CSV || definition.Type == ReportType.JobHistory) ? "csv" : "pdf";
                string safeName = MakeSafeFileName(definition.Name);
                string datePart = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
                string fileName = $"{safeName}_{datePart}.{ext}";
                string fullPath = Path.Combine(folder, fileName);

                File.WriteAllBytes(fullPath, result.Content);

                result.FilePath      = fullPath;
                result.FileSizeBytes = new FileInfo(fullPath).Length;
            }
            catch (Exception ex)
            {
                result.Success      = false;
                result.ErrorMessage = $"فشل حفظ الملف: {ex.Message}";
            }

            return result;
        }

        // ── PDF Generators ────────────────────────────────────────────────────

        private byte[] GenerateDailySummaryPdf(ReportDefinition def)
        {
            var date    = def.FromDate?.Date ?? DateTime.Today;
            var records = PrintCostTracker.Instance.GetTodayRecords();

            if (def.FromDate.HasValue && def.FromDate.Value.Date != DateTime.Today)
                records = PrintCostTracker.Instance.GetRecordsForRange(date, date);

            int     totalJobs   = records.Count;
            int     totalPages  = records.Sum(r => r.Pages * r.Copies);
            decimal totalRev    = records.Sum(r => r.ChargedPrice);
            decimal totalProfit = records.Sum(r => r.Profit);

            // Per-printer grouping
            var printerGroups = records
                .GroupBy(r => r.PrinterName)
                .Select(g => new
                {
                    Printer     = g.Key,
                    Jobs        = g.Count(),
                    Pages       = g.Sum(r => r.Pages * r.Copies),
                    Revenue     = g.Sum(r => r.ChargedPrice),
                    SuccessRate = g.Count() > 0
                                      ? g.Count(r => r.WasSuccessful) * 100.0 / g.Count()
                                      : 0.0
                })
                .ToList();

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(20, Unit.Millimetre);
                    page.DefaultTextStyle(x => x.FontFamily("Tahoma"));

                    page.Header().Element(ComposeHeader(def.CompanyName,
                        $"تقرير اليوم — {date:dd/MM/yyyy}"));

                    page.Content().PaddingTop(8).Column(col =>
                    {
                        col.Spacing(12);

                        // Section 2 — KPI Row
                        col.Item().Row(row =>
                        {
                            row.Spacing(8);
                            row.RelativeItem().Element(KpiCard("إجمالي الوظائف",   totalJobs.ToString()));
                            row.RelativeItem().Element(KpiCard("الصفحات المطبوعة", totalPages.ToString()));
                            row.RelativeItem().Element(KpiCard("الإيراد",          $"{totalRev:F2} ج.م"));
                            row.RelativeItem().Element(KpiCard("الربح",            $"{totalProfit:F2} ج.م"));
                        });

                        // Section 3 — Jobs Table
                        col.Item().Column(tableCol =>
                        {
                            tableCol.Item()
                                .PaddingBottom(6)
                                .Text("تفاصيل الوظائف")
                                .FontSize(13).Bold().FontColor("#1E3A5F");

                            tableCol.Item().Table(table =>
                            {
                                table.ColumnsDefinition(cols =>
                                {
                                    cols.RelativeColumn(1.2f);
                                    cols.RelativeColumn(2.5f);
                                    cols.RelativeColumn(2f);
                                    cols.RelativeColumn(0.8f);
                                    cols.RelativeColumn(1.2f);
                                    cols.RelativeColumn(1.2f);
                                    cols.RelativeColumn(1f);
                                });

                                // Header row
                                static IContainer HeaderCell(IContainer c) =>
                                    c.Background("#1E3A5F").Padding(5);

                                table.Header(header =>
                                {
                                    header.Cell().Element(HeaderCell).Text("الوقت").FontColor(Colors.White).FontSize(9).Bold();
                                    header.Cell().Element(HeaderCell).Text("اسم الوظيفة").FontColor(Colors.White).FontSize(9).Bold();
                                    header.Cell().Element(HeaderCell).Text("الطابعة").FontColor(Colors.White).FontSize(9).Bold();
                                    header.Cell().Element(HeaderCell).Text("الصفحات").FontColor(Colors.White).FontSize(9).Bold();
                                    header.Cell().Element(HeaderCell).Text("التكلفة").FontColor(Colors.White).FontSize(9).Bold();
                                    header.Cell().Element(HeaderCell).Text("السعر").FontColor(Colors.White).FontSize(9).Bold();
                                    header.Cell().Element(HeaderCell).Text("الحالة").FontColor(Colors.White).FontSize(9).Bold();
                                });

                                // Data rows
                                int rowIndex = 0;
                                foreach (var r in records)
                                {
                                    string bg        = rowIndex % 2 == 0 ? "#F8FAFC" : Colors.White;
                                    string statusTxt = r.WasSuccessful ? "ناجح" : "فاشل";
                                    string statusClr = r.WasSuccessful ? Colors.Black : Colors.Red.Medium;

                                    IContainer DataCell(IContainer c) =>
                                        c.Background(bg).Padding(4);

                                    table.Cell().Element(DataCell)
                                        .Text(r.StartedAt.ToString("HH:mm")).FontSize(8).FontColor(statusClr);
                                    table.Cell().Element(DataCell)
                                        .Text(r.JobName).FontSize(8).FontColor(statusClr);
                                    table.Cell().Element(DataCell)
                                        .Text(r.PrinterName).FontSize(8).FontColor(statusClr);
                                    table.Cell().Element(DataCell)
                                        .Text((r.Pages * r.Copies).ToString()).FontSize(8).FontColor(statusClr);
                                    table.Cell().Element(DataCell)
                                        .Text($"{r.TotalCost:F2}").FontSize(8).FontColor(statusClr);
                                    table.Cell().Element(DataCell)
                                        .Text($"{r.ChargedPrice:F2}").FontSize(8).FontColor(statusClr);
                                    table.Cell().Element(DataCell)
                                        .Text(statusTxt).FontSize(8).FontColor(statusClr);

                                    rowIndex++;
                                }
                            });
                        });

                        // Section 4 — Per-Printer Summary
                        if (printerGroups.Any())
                        {
                            col.Item().Column(pCol =>
                            {
                                pCol.Item()
                                    .PaddingBottom(6)
                                    .Text("ملخص الطابعات")
                                    .FontSize(13).Bold().FontColor("#1E3A5F");

                                pCol.Item().Table(table =>
                                {
                                    table.ColumnsDefinition(cols =>
                                    {
                                        cols.RelativeColumn(3f);
                                        cols.RelativeColumn(1.5f);
                                        cols.RelativeColumn(1.5f);
                                        cols.RelativeColumn(2f);
                                        cols.RelativeColumn(2f);
                                    });

                                    static IContainer PHCell(IContainer c) =>
                                        c.Background("#1E3A5F").Padding(5);

                                    table.Header(header =>
                                    {
                                        header.Cell().Element(PHCell).Text("الطابعة").FontColor(Colors.White).FontSize(9).Bold();
                                        header.Cell().Element(PHCell).Text("الوظائف").FontColor(Colors.White).FontSize(9).Bold();
                                        header.Cell().Element(PHCell).Text("الصفحات").FontColor(Colors.White).FontSize(9).Bold();
                                        header.Cell().Element(PHCell).Text("الإيراد").FontColor(Colors.White).FontSize(9).Bold();
                                        header.Cell().Element(PHCell).Text("معدل النجاح").FontColor(Colors.White).FontSize(9).Bold();
                                    });

                                    int pRow = 0;
                                    foreach (var pg in printerGroups)
                                    {
                                        string bg = pRow % 2 == 0 ? "#F8FAFC" : Colors.White;
                                        IContainer PDCell(IContainer c) => c.Background(bg).Padding(4);

                                        table.Cell().Element(PDCell).Text(pg.Printer).FontSize(8);
                                        table.Cell().Element(PDCell).Text(pg.Jobs.ToString()).FontSize(8);
                                        table.Cell().Element(PDCell).Text(pg.Pages.ToString()).FontSize(8);
                                        table.Cell().Element(PDCell).Text($"{pg.Revenue:F2} ج.م").FontSize(8);
                                        table.Cell().Element(PDCell).Text($"{pg.SuccessRate:F0}%").FontSize(8);
                                        pRow++;
                                    }
                                });
                            });
                        }
                    });

                    page.Footer().Element(ComposeFooter());
                });
            }).GeneratePdf();
        }

        private byte[] GenerateWeeklySummaryPdf(ReportDefinition def)
        {
            var toDate   = def.ToDate?.Date   ?? DateTime.Today;
            var fromDate = def.FromDate?.Date ?? toDate.AddDays(-6);

            var revReport = RevenueAnalytics.Instance.GetReport(fromDate, toDate);

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(20, Unit.Millimetre);
                    page.DefaultTextStyle(x => x.FontFamily("Tahoma"));

                    page.Header().Element(ComposeHeader(def.CompanyName,
                        $"التقرير الأسبوعي — {fromDate:dd/MM/yyyy} إلى {toDate:dd/MM/yyyy}"));

                    page.Content().PaddingTop(8).Column(col =>
                    {
                        col.Spacing(12);

                        // Summary KPIs
                        col.Item().Row(row =>
                        {
                            row.Spacing(8);
                            row.RelativeItem().Element(KpiCard("إجمالي الوظائف",   revReport.TotalJobs.ToString()));
                            row.RelativeItem().Element(KpiCard("الصفحات المطبوعة", revReport.TotalPages.ToString()));
                            row.RelativeItem().Element(KpiCard("الإيراد",          $"{revReport.TotalRevenue:F2} ج.م"));
                            row.RelativeItem().Element(KpiCard("الربح",            $"{revReport.TotalProfit:F2} ج.م"));
                        });

                        // Daily breakdown table
                        col.Item().Column(tCol =>
                        {
                            tCol.Item().PaddingBottom(6).Text("التفصيل اليومي").FontSize(13).Bold().FontColor("#1E3A5F");

                            tCol.Item().Table(table =>
                            {
                                table.ColumnsDefinition(cols =>
                                {
                                    cols.RelativeColumn(2f);
                                    cols.RelativeColumn(1.5f);
                                    cols.RelativeColumn(1.5f);
                                    cols.RelativeColumn(2f);
                                    cols.RelativeColumn(2f);
                                    cols.RelativeColumn(2f);
                                });

                                static IContainer WHCell(IContainer c) =>
                                    c.Background("#1E3A5F").Padding(5);

                                table.Header(h =>
                                {
                                    h.Cell().Element(WHCell).Text("التاريخ").FontColor(Colors.White).FontSize(9).Bold();
                                    h.Cell().Element(WHCell).Text("الوظائف").FontColor(Colors.White).FontSize(9).Bold();
                                    h.Cell().Element(WHCell).Text("الصفحات").FontColor(Colors.White).FontSize(9).Bold();
                                    h.Cell().Element(WHCell).Text("الإيراد").FontColor(Colors.White).FontSize(9).Bold();
                                    h.Cell().Element(WHCell).Text("التكلفة").FontColor(Colors.White).FontSize(9).Bold();
                                    h.Cell().Element(WHCell).Text("الربح").FontColor(Colors.White).FontSize(9).Bold();
                                });

                                int rowIdx = 0;
                                foreach (var dp in revReport.DailyBreakdown)
                                {
                                    string bg = rowIdx % 2 == 0 ? "#F8FAFC" : Colors.White;
                                    IContainer WDCell(IContainer c) => c.Background(bg).Padding(4);

                                    table.Cell().Element(WDCell).Text(dp.Date.ToString("ddd dd/MM")).FontSize(8);
                                    table.Cell().Element(WDCell).Text(dp.JobCount.ToString()).FontSize(8);
                                    table.Cell().Element(WDCell).Text(dp.PageCount.ToString()).FontSize(8);
                                    table.Cell().Element(WDCell).Text($"{dp.Revenue:F2}").FontSize(8);
                                    table.Cell().Element(WDCell).Text($"{dp.Cost:F2}").FontSize(8);
                                    table.Cell().Element(WDCell).Text($"{dp.Profit:F2}").FontSize(8);
                                    rowIdx++;
                                }

                                // Totals row
                                IContainer TotCell(IContainer c) => c.Background("#E2E8F0").Padding(4);
                                table.Cell().Element(TotCell).Text("الإجمالي").FontSize(9).Bold();
                                table.Cell().Element(TotCell).Text(revReport.TotalJobs.ToString()).FontSize(9).Bold();
                                table.Cell().Element(TotCell).Text(revReport.TotalPages.ToString()).FontSize(9).Bold();
                                table.Cell().Element(TotCell).Text($"{revReport.TotalRevenue:F2}").FontSize(9).Bold();
                                table.Cell().Element(TotCell).Text($"{revReport.TotalCost:F2}").FontSize(9).Bold();
                                table.Cell().Element(TotCell).Text($"{revReport.TotalProfit:F2}").FontSize(9).Bold();
                            });
                        });
                    });

                    page.Footer().Element(ComposeFooter());
                });
            }).GeneratePdf();
        }

        private byte[] GeneratePrinterHealthPdf(ReportDefinition def)
        {
            var metrics = PrinterEfficiencyReport.Instance.GetAllPrinterMetrics();

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(20, Unit.Millimetre);
                    page.DefaultTextStyle(x => x.FontFamily("Tahoma"));

                    page.Header().Element(ComposeHeader(def.CompanyName, "تقرير صحة الطابعات"));

                    page.Content().PaddingTop(8).Column(col =>
                    {
                        col.Spacing(10);

                        if (!metrics.Any())
                        {
                            col.Item().Text("لا توجد بيانات طابعات متاحة لهذا اليوم.")
                                .FontSize(11).FontColor("#64748B");
                            return;
                        }

                        foreach (var m in metrics)
                        {
                            string gradeColor = m.EfficiencyGrade switch
                            {
                                "A" => "#16A34A",
                                "B" => "#2563EB",
                                "C" => "#D97706",
                                "D" => "#DC2626",
                                _   => "#7F1D1D"
                            };

                            int barFilled = Math.Max(0, Math.Min(100, m.HealthScore));
                            int barEmpty  = 100 - barFilled;

                            col.Item().Border(1).BorderColor("#CBD5E1").Padding(10).Column(card =>
                            {
                                // Row 1: printer name + grade badge
                                card.Item().Row(r =>
                                {
                                    r.RelativeItem().Text(m.PrinterName).FontSize(12).Bold().FontColor("#0F172A");
                                    r.AutoItem().Text($" [{m.EfficiencyGrade}]")
                                        .FontSize(12).Bold().FontColor(gradeColor);
                                });

                                // Row 2: 4 metric columns
                                card.Item().PaddingTop(6).Row(r =>
                                {
                                    r.Spacing(6);
                                    r.RelativeItem().Element(SmallMetric("الوظائف",      m.TotalJobsToday.ToString()));
                                    r.RelativeItem().Element(SmallMetric("الصفحات",      m.TotalPagesToday.ToString()));
                                    r.RelativeItem().Element(SmallMetric("معدل النجاح",  $"{m.SuccessRateToday:F0}%"));
                                    r.RelativeItem().Element(SmallMetric("سرعة الطباعة", $"{m.AveragePagesPerMinute:F1} ص/د"));
                                });

                                // Row 3: health score bar
                                card.Item().PaddingTop(6).Column(barCol =>
                                {
                                    barCol.Item().Text($"مؤشر الصحة: {m.HealthScore}/100")
                                        .FontSize(9).FontColor("#475569");
                                    barCol.Item().PaddingTop(3).Height(10).Row(bar =>
                                    {
                                        if (barFilled > 0)
                                            bar.RelativeItem(barFilled).Background(gradeColor).Height(10);
                                        if (barEmpty > 0)
                                            bar.RelativeItem(barEmpty).Background("#E2E8F0").Height(10);
                                    });
                                });

                                // Row 4: Arabic status text
                                card.Item().PaddingTop(4).Text(m.StatusArabic)
                                    .FontSize(9).FontColor("#64748B").Italic();
                            });
                        }
                    });

                    page.Footer().Element(ComposeFooter());
                });
            }).GeneratePdf();
        }

        private byte[] GenerateCostAnalysisPdf(ReportDefinition def)
        {
            var toDate   = def.ToDate?.Date   ?? DateTime.Today;
            var fromDate = def.FromDate?.Date ?? toDate.AddDays(-6);

            var revReport    = RevenueAnalytics.Instance.GetReport(fromDate, toDate);
            var daily        = revReport.DailyBreakdown;
            decimal maxRev   = daily.Any() ? daily.Max(d => d.Revenue) : 1m;
            if (maxRev <= 0) maxRev = 1m;

            // Most expensive printer
            var perPrinter = PrintCostTracker.Instance.GetPerPrinterStats(fromDate, toDate);
            var topPrinter = perPrinter.OrderByDescending(kv => kv.Value.Cost).FirstOrDefault();

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(20, Unit.Millimetre);
                    page.DefaultTextStyle(x => x.FontFamily("Tahoma"));

                    page.Header().Element(ComposeHeader(def.CompanyName,
                        $"تحليل التكاليف — {fromDate:dd/MM/yyyy} إلى {toDate:dd/MM/yyyy}"));

                    page.Content().PaddingTop(8).Column(col =>
                    {
                        col.Spacing(12);

                        // Revenue vs Cost comparison bars
                        col.Item().Text("مقارنة الإيراد والتكلفة").FontSize(13).Bold().FontColor("#1E3A5F");

                        col.Item().Column(barCol =>
                        {
                            barCol.Spacing(4);
                            foreach (var dp in daily)
                            {
                                decimal revFrac  = maxRev > 0 ? dp.Revenue / maxRev : 0m;
                                decimal costFrac = maxRev > 0 ? dp.Cost    / maxRev : 0m;
                                int     revW     = Math.Max(1, (int)(revFrac  * 200));
                                int     costW    = Math.Max(1, (int)(costFrac * 200));

                                barCol.Item().Row(r =>
                                {
                                    r.ConstantItem(60).Text(dp.Date.ToString("dd/MM")).FontSize(8).FontColor("#475569");
                                    r.AutoItem().Column(bc =>
                                    {
                                        bc.Item().Row(br =>
                                        {
                                            br.ConstantItem(revW).Height(6).Background("#2563EB");
                                            br.RelativeItem();
                                        });
                                        bc.Item().PaddingTop(2).Row(br =>
                                        {
                                            br.ConstantItem(costW).Height(6).Background("#DC2626");
                                            br.RelativeItem();
                                        });
                                    });
                                    r.ConstantItem(80).Text($"{dp.Revenue:F0} / {dp.Cost:F0}").FontSize(7).FontColor("#64748B");
                                });
                            }

                            // Legend
                            barCol.Item().PaddingTop(4).Row(leg =>
                            {
                                leg.AutoItem().Width(12).Height(8).Background("#2563EB");
                                leg.AutoItem().PaddingLeft(4).Text("إيراد").FontSize(8);
                                leg.AutoItem().PaddingLeft(12).Width(12).Height(8).Background("#DC2626");
                                leg.AutoItem().PaddingLeft(4).Text("تكلفة").FontSize(8);
                            });
                        });

                        // Daily cost breakdown table
                        col.Item().Column(tCol =>
                        {
                            tCol.Item().PaddingBottom(6).Text("جدول التكاليف اليومية").FontSize(13).Bold().FontColor("#1E3A5F");

                            tCol.Item().Table(table =>
                            {
                                table.ColumnsDefinition(cols =>
                                {
                                    cols.RelativeColumn(2f);
                                    cols.RelativeColumn(2f);
                                    cols.RelativeColumn(2f);
                                    cols.RelativeColumn(2f);
                                    cols.RelativeColumn(2f);
                                });

                                static IContainer CHCell(IContainer c) =>
                                    c.Background("#1E3A5F").Padding(5);

                                table.Header(h =>
                                {
                                    h.Cell().Element(CHCell).Text("التاريخ").FontColor(Colors.White).FontSize(9).Bold();
                                    h.Cell().Element(CHCell).Text("الإيراد").FontColor(Colors.White).FontSize(9).Bold();
                                    h.Cell().Element(CHCell).Text("التكلفة").FontColor(Colors.White).FontSize(9).Bold();
                                    h.Cell().Element(CHCell).Text("الربح").FontColor(Colors.White).FontSize(9).Bold();
                                    h.Cell().Element(CHCell).Text("هامش الربح").FontColor(Colors.White).FontSize(9).Bold();
                                });

                                int ri = 0;
                                foreach (var dp in daily)
                                {
                                    string bg = ri % 2 == 0 ? "#F8FAFC" : Colors.White;
                                    IContainer CDCell(IContainer c) => c.Background(bg).Padding(4);

                                    table.Cell().Element(CDCell).Text(dp.Date.ToString("dd/MM/yyyy")).FontSize(8);
                                    table.Cell().Element(CDCell).Text($"{dp.Revenue:F2}").FontSize(8);
                                    table.Cell().Element(CDCell).Text($"{dp.Cost:F2}").FontSize(8);
                                    table.Cell().Element(CDCell).Text($"{dp.Profit:F2}").FontSize(8);
                                    table.Cell().Element(CDCell).Text($"{dp.MarginPercent:F1}%").FontSize(8);
                                    ri++;
                                }
                            });
                        });

                        // Most expensive printer
                        if (!string.IsNullOrEmpty(topPrinter.Key))
                        {
                            col.Item().Border(1).BorderColor("#CBD5E1").Padding(10).Column(pc =>
                            {
                                pc.Item().Text("أعلى تكلفة طابعة").FontSize(12).Bold().FontColor("#1E3A5F");
                                pc.Item().PaddingTop(4).Text(topPrinter.Key).FontSize(11).Bold();
                                pc.Item().PaddingTop(2).Text($"التكلفة: {topPrinter.Value.Cost:F2} ج.م | الإيراد: {topPrinter.Value.Revenue:F2} ج.م | الوظائف: {topPrinter.Value.Jobs}").FontSize(9).FontColor("#475569");
                            });
                        }
                    });

                    page.Footer().Element(ComposeFooter());
                });
            }).GeneratePdf();
        }

        private byte[] GenerateJobHistoryCsv(ReportDefinition def)
        {
            var toDate   = def.ToDate?.Date   ?? DateTime.Today;
            var fromDate = def.FromDate?.Date ?? toDate;

            var records = PrintCostTracker.Instance.GetRecordsForRange(fromDate, toDate);

            var sb = new StringBuilder();
            // UTF-8 BOM is added via encoding below — build the string content here
            sb.AppendLine("رقم,الوقت,اسم الوظيفة,الطابعة,الصفحات,النسخ,ملون,ناجحة,التكلفة,السعر,الربح,المدة(ث)");

            int i = 1;
            foreach (var r in records)
            {
                sb.AppendLine(string.Join(",",
                    i++,
                    r.StartedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    CsvEscape(r.JobName),
                    CsvEscape(r.PrinterName),
                    r.Pages,
                    r.Copies,
                    r.IsColor    ? "نعم" : "لا",
                    r.WasSuccessful ? "نعم" : "لا",
                    r.TotalCost.ToString("F2"),
                    r.ChargedPrice.ToString("F2"),
                    r.Profit.ToString("F2"),
                    ((int)r.PrintDuration.TotalSeconds)
                ));
            }

            // UTF-8 with BOM
            byte[] bom     = new byte[] { 0xEF, 0xBB, 0xBF };
            byte[] content = Encoding.UTF8.GetBytes(sb.ToString());
            byte[] result  = new byte[bom.Length + content.Length];
            Buffer.BlockCopy(bom, 0, result, 0, bom.Length);
            Buffer.BlockCopy(content, 0, result, bom.Length, content.Length);
            return result;
        }

        // ── Reusable QuestPDF fragments ────────────────────────────────────────

        private static Action<IContainer> ComposeHeader(string companyName, string subtitle)
        {
            return container =>
            {
                container
                    .Background("#1E3A5F")
                    .Padding(14)
                    .Row(row =>
                    {
                        // Logo placeholder
                        row.ConstantItem(44).Height(44).Background("#2563EB")
                            .AlignCenter().AlignMiddle()
                            .Text("A").FontColor(Colors.White).FontSize(20).Bold();

                        row.RelativeItem().PaddingLeft(12).Column(col =>
                        {
                            col.Item().AlignRight().Text(companyName)
                                .FontSize(18).Bold().FontColor(Colors.White).FontFamily("Tahoma");
                            col.Item().AlignRight().Text(subtitle)
                                .FontSize(10).FontColor("#CBD5E1").FontFamily("Tahoma");
                        });
                    });
            };
        }

        private static Action<IContainer> ComposeFooter()
        {
            return container =>
            {
                container
                    .BorderTop(1).BorderColor("#CBD5E1")
                    .PaddingTop(6)
                    .Row(row =>
                    {
                        row.RelativeItem().Text($"تم الإنشاء: {DateTime.Now:dd/MM/yyyy HH:mm:ss}")
                            .FontSize(8).FontColor("#94A3B8").FontFamily("Tahoma");
                        row.AutoItem().Text(txt =>
                        {
                            txt.Span("صفحة ").FontSize(8).FontColor("#94A3B8").FontFamily("Tahoma");
                            txt.CurrentPageNumber().FontSize(8).FontColor("#94A3B8").FontFamily("Tahoma");
                            txt.Span(" من ").FontSize(8).FontColor("#94A3B8").FontFamily("Tahoma");
                            txt.TotalPages().FontSize(8).FontColor("#94A3B8").FontFamily("Tahoma");
                        });
                    });
            };
        }

        private static Action<IContainer> KpiCard(string label, string value)
        {
            return container =>
            {
                container
                    .Border(1).BorderColor("#CBD5E1")
                    .Padding(10)
                    .Column(col =>
                    {
                        col.Item().AlignCenter().Text(label)
                            .FontSize(9).FontColor("#64748B").FontFamily("Tahoma");
                        col.Item().AlignCenter().Text(value)
                            .FontSize(20).Bold().FontColor("#1E3A5F").FontFamily("Tahoma");
                    });
            };
        }

        private static Action<IContainer> SmallMetric(string label, string value)
        {
            return container =>
            {
                container
                    .Background("#F8FAFC")
                    .Border(1).BorderColor("#E2E8F0")
                    .Padding(6)
                    .Column(col =>
                    {
                        col.Item().AlignCenter().Text(label).FontSize(8).FontColor("#64748B").FontFamily("Tahoma");
                        col.Item().AlignCenter().Text(value).FontSize(10).Bold().FontColor("#0F172A").FontFamily("Tahoma");
                    });
            };
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static string CsvEscape(string value)
        {
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
                return $"\"{value.Replace("\"", "\"\"")}\"";
            return value;
        }

        private static string MakeSafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb      = new StringBuilder();
            foreach (char c in name)
                sb.Append(invalid.Contains(c) ? '_' : c);
            return sb.Length > 0 ? sb.ToString() : "report";
        }
    }
}
