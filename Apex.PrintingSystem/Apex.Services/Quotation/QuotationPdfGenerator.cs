using System;
using System.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Apex.Services.Quotation
{
    // ══════════════════════════════════════════════════════════════════════════
    // DATA MODEL
    // ══════════════════════════════════════════════════════════════════════════

    public class QuotationData
    {
        // ── Header ─────────────────────────────────────────────────────────
        public string  CompanyName       { get; set; } = "أبكس لحلول الطباعة المتكاملة";
        public string  CompanyPhone      { get; set; } = "01099088053";
        public string  QuotationNumber   { get; set; } = "";
        public DateTime QuotationDate    { get; set; } = DateTime.Now;
        public DateTime ValidUntil       { get; set; } = DateTime.Now.AddDays(7);

        // ── Customer ───────────────────────────────────────────────────────
        public string  CustomerName      { get; set; } = "";
        public string? CustomerPhone     { get; set; }

        // ── Job Details ────────────────────────────────────────────────────
        public string  JobDescription    { get; set; } = "";
        public int     TotalPages        { get; set; }
        public int     Quantity          { get; set; } = 1;
        public bool    IsDoubleSided     { get; set; }
        public bool    IsColor           { get; set; }
        public string  PaperSize         { get; set; } = "A4";
        public bool    HasCover          { get; set; }
        public bool    HasLamination     { get; set; }

        // ── Pricing ────────────────────────────────────────────────────────
        public decimal InkCostPerPage    { get; set; } = 0.05m;
        public decimal PaperCostPerPage  { get; set; } = 0.02m;
        public decimal SetupCost         { get; set; } = 0m;
        public decimal CoverCost         { get; set; } = 0m;
        public decimal LaminationCost    { get; set; } = 0m;
        public decimal DiscountPercent   { get; set; } = 0m;

        // ── Computed ───────────────────────────────────────────────────────
        public decimal SubTotal          => (InkCostPerPage + PaperCostPerPage) * TotalPages * Quantity
                                          + SetupCost
                                          + (HasCover      ? CoverCost      * Quantity : 0)
                                          + (HasLamination ? LaminationCost * Quantity : 0);

        public decimal DiscountAmount    => SubTotal * (DiscountPercent / 100m);
        public decimal TotalPrice        => SubTotal - DiscountAmount;
        public decimal PricePerCopy      => Quantity > 0 ? TotalPrice / Quantity : TotalPrice;

        // ── Notes ──────────────────────────────────────────────────────────
        public string? Notes             { get; set; }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // QUOTATION PDF GENERATOR
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 📄 QUOTATION PDF GENERATOR
    ///
    /// Generates professional Arabic print quotations as PDF files
    /// using QuestPDF fluent API.
    ///
    /// Usage:
    ///   var pdf = QuotationPdfGenerator.Generate(data);
    ///   File.WriteAllBytes("quotation.pdf", pdf);
    ///
    ///   // Or save to temp file:
    ///   string path = QuotationPdfGenerator.GenerateToTempFile(data);
    /// </summary>
    public static class QuotationPdfGenerator
    {
        // ── Color Palette ─────────────────────────────────────────────────
        private static readonly string PrimaryBlue    = "#2563EB";
        private static readonly string DarkBg         = "#0F172A";
        private static readonly string CardBg         = "#1E293B";
        private static readonly string TextPrimary    = "#F8FAFC";
        private static readonly string TextSecondary  = "#94A3B8";
        private static readonly string AccentGreen    = "#22C55E";
        private static readonly string AccentOrange   = "#F59E0B";
        private static readonly string BorderColor    = "#334155";

        // ── Static initializer ────────────────────────────────────────────
        static QuotationPdfGenerator()
        {
            // QuestPDF community license (free for revenue < $1M)
            QuestPDF.Settings.License = LicenseType.Community;
        }

        // ── Public API ────────────────────────────────────────────────────

        /// <summary>Generate quotation PDF as byte array.</summary>
        public static byte[] Generate(QuotationData data)
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1.5f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(10).FontColor(Colors.Black));

                    page.Content().Column(col =>
                    {
                        // ── Header ────────────────────────────────────────
                        col.Item().Element(ComposeHeader(data));

                        col.Item().Height(12);

                        // ── Customer & Job Info ───────────────────────────
                        col.Item().Element(ComposeInfoSection(data));

                        col.Item().Height(12);

                        // ── Details Table ─────────────────────────────────
                        col.Item().Element(ComposeDetailsTable(data));

                        col.Item().Height(12);

                        // ── Pricing Summary ───────────────────────────────
                        col.Item().Element(ComposePricingSummary(data));

                        // ── Notes ─────────────────────────────────────────
                        if (!string.IsNullOrWhiteSpace(data.Notes))
                        {
                            col.Item().Height(12);
                            col.Item().Element(ComposeNotes(data.Notes));
                        }

                        // ── Footer ────────────────────────────────────────
                        col.Item().Height(16);
                        col.Item().Element(ComposeFooter(data));
                    });
                });
            });

            return document.GeneratePdf();
        }

        /// <summary>Generate quotation PDF to a temp file, return file path.</summary>
        public static string GenerateToTempFile(QuotationData data, string? outputDir = null)
        {
            outputDir ??= Path.GetTempPath();
            Directory.CreateDirectory(outputDir);

            string fileName = $"Quotation_{data.QuotationNumber}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            string filePath = Path.Combine(outputDir, fileName);

            File.WriteAllBytes(filePath, Generate(data));
            return filePath;
        }

        // ── Compose Sections ──────────────────────────────────────────────

        private static Action<IContainer> ComposeHeader(QuotationData data)
        => container => container
            .Background(PrimaryBlue)
            .Padding(16)
            .Row(row =>
            {
                // Company name (Arabic, right-aligned)
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text(data.CompanyName)
                        .FontSize(18).Bold().FontColor(Colors.White);
                    col.Item().Text($"📞 {data.CompanyPhone}")
                        .FontSize(10).FontColor("#BFDBFE");
                });

                // Quotation number + date (left side)
                row.ConstantItem(160).Column(col =>
                {
                    col.Item().Background(Colors.White).Padding(8).Column(inner =>
                    {
                        inner.Item().Text("عرض سعر").FontSize(14).Bold()
                            .FontColor(PrimaryBlue);
                        inner.Item().Text($"رقم: {data.QuotationNumber}")
                            .FontSize(9).FontColor("#374151");
                        inner.Item().Text($"التاريخ: {data.QuotationDate:dd/MM/yyyy}")
                            .FontSize(9).FontColor("#374151");
                        inner.Item().Text($"صالح حتى: {data.ValidUntil:dd/MM/yyyy}")
                            .FontSize(9).FontColor("#EF4444");
                    });
                });
            });

        private static Action<IContainer> ComposeInfoSection(QuotationData data)
        => container => container
            .Border(1).BorderColor(BorderColor).Padding(12)
            .Row(row =>
            {
                // Customer info
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text("بيانات العميل").Bold().FontSize(11)
                        .FontColor(PrimaryBlue);
                    col.Item().Height(4);
                    col.Item().Text($"الاسم: {data.CustomerName}").FontSize(10);
                    if (!string.IsNullOrEmpty(data.CustomerPhone))
                        col.Item().Text($"الهاتف: {data.CustomerPhone}").FontSize(10);
                });

                row.ConstantItem(1).Background(BorderColor);

                // Job info
                row.RelativeItem().PaddingLeft(12).Column(col =>
                {
                    col.Item().Text("تفاصيل المشروع").Bold().FontSize(11)
                        .FontColor(PrimaryBlue);
                    col.Item().Height(4);
                    col.Item().Text($"الوصف: {data.JobDescription}").FontSize(10);
                    col.Item().Text($"حجم الورق: {data.PaperSize}").FontSize(10);
                    col.Item().Text($"نوع الطباعة: {(data.IsColor ? "ألوان" : "أبيض وأسود")}").FontSize(10);
                    col.Item().Text($"وجه الطباعة: {(data.IsDoubleSided ? "وجهان" : "وجه واحد")}").FontSize(10);
                });
            });

        private static Action<IContainer> ComposeDetailsTable(QuotationData data)
        => container =>
        {
            container.Column(col =>
            {
                // Table header
                col.Item().Background(PrimaryBlue).Padding(8)
                    .Row(row =>
                    {
                        row.RelativeItem(4).Text("البيان").Bold().FontColor(Colors.White);
                        row.RelativeItem(1).AlignCenter().Text("الكمية").Bold().FontColor(Colors.White);
                        row.RelativeItem(2).AlignCenter().Text("سعر الوحدة (ج.م)").Bold().FontColor(Colors.White);
                        row.RelativeItem(2).AlignCenter().Text("الإجمالي (ج.م)").Bold().FontColor(Colors.White);
                    });

                // Table rows
                int rowIndex = 0;
                void AddRow(string item, string qty, string unitPrice, string total)
                {
                    string bg = rowIndex++ % 2 == 0 ? "#F8FAFC" : Colors.White;
                    col.Item().Background(bg).BorderBottom(1).BorderColor(BorderColor).Padding(8)
                        .Row(row =>
                        {
                            row.RelativeItem(4).Text(item).FontSize(10);
                            row.RelativeItem(1).AlignCenter().Text(qty).FontSize(10);
                            row.RelativeItem(2).AlignCenter().Text(unitPrice).FontSize(10);
                            row.RelativeItem(2).AlignCenter().Text(total).FontSize(10);
                        });
                }

                decimal printCost = (data.InkCostPerPage + data.PaperCostPerPage) * data.TotalPages;
                AddRow(
                    $"طباعة {data.TotalPages} صفحة ({data.PaperSize})",
                    data.Quantity.ToString(),
                    $"{printCost:F2}",
                    $"{printCost * data.Quantity:F2}");

                if (data.HasCover && data.CoverCost > 0)
                    AddRow("غلاف", data.Quantity.ToString(),
                        $"{data.CoverCost:F2}", $"{data.CoverCost * data.Quantity:F2}");

                if (data.HasLamination && data.LaminationCost > 0)
                    AddRow("تغليف لاميناتور", data.Quantity.ToString(),
                        $"{data.LaminationCost:F2}", $"{data.LaminationCost * data.Quantity:F2}");

                if (data.SetupCost > 0)
                    AddRow("رسوم التجهيز والإعداد", "1",
                        $"{data.SetupCost:F2}", $"{data.SetupCost:F2}");
            });
        };

        private static Action<IContainer> ComposePricingSummary(QuotationData data)
        => container =>
        {
            container.AlignRight().Width(240).Column(col =>
            {
                void SummaryRow(string label, string value, bool bold = false, string? color = null)
                {
                    col.Item()
                        .BorderBottom(1).BorderColor(BorderColor)
                        .Padding(6)
                        .Row(row =>
                        {
                            var labelDesc = row.RelativeItem().Text(label)
                                .FontSize(10)
                                .FontColor(color ?? Colors.Black);
                            if (bold) labelDesc.Bold();

                            var valueDesc = row.ConstantItem(100).AlignRight().Text(value)
                                .FontSize(10)
                                .FontColor(color ?? Colors.Black);
                            if (bold) valueDesc.Bold();
                        });
                }

                SummaryRow("المجموع الجزئي", $"{data.SubTotal:F2} ج.م");

                if (data.DiscountPercent > 0)
                    SummaryRow($"خصم ({data.DiscountPercent:F0}%)", $"- {data.DiscountAmount:F2} ج.م",
                        color: "#EF4444");

                // Total row with colored background
                col.Item().Background(PrimaryBlue).Padding(8)
                    .Row(row =>
                    {
                        row.RelativeItem().Text("الإجمالي النهائي").Bold().FontSize(12).FontColor(Colors.White);
                        row.ConstantItem(100).AlignRight()
                            .Text($"{data.TotalPrice:F2} ج.م").Bold().FontSize(12).FontColor(Colors.White);
                    });

                if (data.Quantity > 1)
                {
                    col.Item().Background("#F0FDF4").Padding(6)
                        .Row(row =>
                        {
                            row.RelativeItem().Text("السعر للنسخة الواحدة").FontSize(10)
                                .FontColor(AccentGreen);
                            row.ConstantItem(100).AlignRight()
                                .Text($"{data.PricePerCopy:F2} ج.م").Bold().FontSize(10)
                                .FontColor(AccentGreen);
                        });
                }
            });
        };

        private static Action<IContainer> ComposeNotes(string notes)
        => container => container
            .Background("#FFFBEB")
            .Border(1).BorderColor(AccentOrange)
            .Padding(10)
            .Column(col =>
            {
                col.Item().Text("ملاحظات:").Bold().FontSize(10).FontColor(AccentOrange);
                col.Item().Height(4);
                col.Item().Text(notes).FontSize(10);
            });

        private static Action<IContainer> ComposeFooter(QuotationData data)
        => container => container
            .BorderTop(2).BorderColor(PrimaryBlue)
            .PaddingTop(8)
            .Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text("شروط وأحكام").Bold().FontSize(9).FontColor(PrimaryBlue);
                    col.Item().Text("• العرض ساري لمدة 7 أيام من تاريخه").FontSize(8).FontColor("#6B7280");
                    col.Item().Text("• الأسعار تشمل ضريبة القيمة المضافة").FontSize(8).FontColor("#6B7280");
                    col.Item().Text("• يُرجى إرجاع نسخة موقعة للتأكيد").FontSize(8).FontColor("#6B7280");
                });

                row.ConstantItem(160).Column(col =>
                {
                    col.Item().Text("توقيع العميل").Bold().FontSize(9).FontColor(PrimaryBlue);
                    col.Item().Height(30).Border(1).BorderColor(BorderColor);
                    col.Item().Height(4);
                    col.Item().Text("الاسم والتاريخ").FontSize(8).FontColor("#9CA3AF");
                });
            });
    }
}
