using Apex.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Apex.Services
{
    /// <summary>
    /// 📊 Enhanced Quotation Service
    /// 
    /// Provides advanced pricing calculations with:
    /// - Per-copy cost breakdown
    /// - Volume discounts
    /// - Multiple quantity comparison
    /// - Detailed cost analysis
    /// </summary>
    public class EnhancedQuotationService
    {
        #region Quick Quotation

        /// <summary>
        /// Creates a quick quotation with minimal inputs.
        /// </summary>
        /// <param name="pages">Number of pages</param>
        /// <param name="quantity">Number of copies</param>
        /// <param name="isDoubleSided">Double-sided printing</param>
        /// <param name="isColor">Color printing</param>
        /// <returns>Enhanced quotation with full breakdown</returns>
        public EnhancedQuotation CreateQuickQuote(
            int pages,
            int quantity,
            bool isDoubleSided = false,
            bool isColor = false)
        {
            var quote = new EnhancedQuotation
            {
                TotalPages = pages,
                Quantity = quantity,
                IsDoubleSided = isDoubleSided,
                IsColor = isColor,
                Paper = new PaperType("A4 عادي 80gsm", 0.10m),
                InkCostPerPage = isColor ? 0.25m : 0.05m,
                ProfitMarginPercent = 30,
                VolumeDiscounts = DefaultVolumeDiscounts.GetStandardTiers()
            };

            quote.Calculate();
            return quote;
        }

        #endregion

        #region Full Quotation

        /// <summary>
        /// Creates a full quotation with all options.
        /// </summary>
        public EnhancedQuotation CreateFullQuote(
            int pages,
            int quantity,
            bool isDoubleSided,
            bool isColor,
            PaperType paper,
            decimal inkCostPerPage,
            CoverOptions? cover,
            List<FinishingService> finishingServices,
            decimal profitMarginPercent,
            List<VolumeDiscount>? volumeDiscounts = null,
            decimal setupCost = 0,
            string customerName = "",
            string jobDescription = "")
        {
            var quote = new EnhancedQuotation
            {
                TotalPages = pages,
                Quantity = quantity,
                IsDoubleSided = isDoubleSided,
                IsColor = isColor,
                Paper = paper,
                InkCostPerPage = inkCostPerPage,
                Cover = cover,
                FinishingServices = finishingServices ?? new List<FinishingService>(),
                ProfitMarginPercent = profitMarginPercent,
                VolumeDiscounts = volumeDiscounts ?? DefaultVolumeDiscounts.GetStandardTiers(),
                SetupCost = setupCost,
                CustomerName = customerName,
                JobDescription = jobDescription
            };

            quote.Calculate();
            return quote;
        }

        #endregion

        #region Comparison & Analysis

        /// <summary>
        /// Compares prices for different quantities.
        /// </summary>
        public string CompareQuantities(EnhancedQuotation baseQuote, params int[] quantities)
        {
            var sb = new StringBuilder();
            sb.AppendLine("╔══════════════════════════════════════════════════════════════╗");
            sb.AppendLine("║           مقارنة الأسعار حسب الكمية                          ║");
            sb.AppendLine("║           QUANTITY PRICE COMPARISON                          ║");
            sb.AppendLine("╚══════════════════════════════════════════════════════════════╝");
            sb.AppendLine();

            var comparisons = new List<(int qty, EnhancedQuotation quote)>();

            foreach (var qty in quantities.OrderBy(q => q))
            {
                var quote = CreateFullQuote(
                    baseQuote.TotalPages,
                    qty,
                    baseQuote.IsDoubleSided,
                    baseQuote.IsColor,
                    baseQuote.Paper,
                    baseQuote.InkCostPerPage,
                    baseQuote.Cover,
                    baseQuote.FinishingServices,
                    baseQuote.ProfitMarginPercent,
                    baseQuote.VolumeDiscounts,
                    baseQuote.SetupCost);

                comparisons.Add((qty, quote));
            }

            sb.AppendLine("┌──────────┬────────────────┬────────────────┬───────────┬──────────┐");
            sb.AppendLine("│ الكمية   │ سعر النسخة     │ الإجمالي       │ الخصم     │ التوفير  │");
            sb.AppendLine("│ Quantity │ Per Copy       │ Total          │ Discount  │ vs 1 pcs │");
            sb.AppendLine("├──────────┼────────────────┼────────────────┼───────────┼──────────┤");

            var singleCopyPrice = comparisons.FirstOrDefault(c => c.qty == 1).quote?.PerCopyCost.TotalPrice 
                ?? comparisons.First().quote.PerCopyCost.TotalPrice;

            foreach (var (qty, quote) in comparisons)
            {
                var savings = singleCopyPrice - quote.PerCopyCost.PriceAfterDiscount;
                var savingsPercent = singleCopyPrice > 0 ? (savings / singleCopyPrice) * 100 : 0;
                
                sb.AppendLine($"│ {qty,8} │ {quote.PerCopyCost.PriceAfterDiscount,12:F2} EGP │ {quote.TotalCost.FinalTotal,12:F2} EGP │ {quote.AppliedDiscountPercent,7:F1}%  │ {savingsPercent,6:F1}%  │");
            }

            sb.AppendLine("└──────────┴────────────────┴────────────────┴───────────┴──────────┘");
            sb.AppendLine();

            // Find best value
            var bestValue = comparisons
                .Where(c => c.qty > 1)
                .OrderBy(c => c.quote.PerCopyCost.PriceAfterDiscount)
                .FirstOrDefault();

            if (bestValue.quote != null)
            {
                sb.AppendLine($"💡 أفضل قيمة: {bestValue.qty} نسخة بسعر {bestValue.quote.PerCopyCost.PriceAfterDiscount:F2} EGP للنسخة");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Calculates break-even quantity for setup costs.
        /// </summary>
        public (int quantity, decimal pricePerCopy) CalculateBreakEven(
            EnhancedQuotation quote,
            decimal targetPricePerCopy)
        {
            // Find quantity where price per copy reaches target
            for (int qty = 1; qty <= 10000; qty++)
            {
                var testQuote = CreateFullQuote(
                    quote.TotalPages,
                    qty,
                    quote.IsDoubleSided,
                    quote.IsColor,
                    quote.Paper,
                    quote.InkCostPerPage,
                    quote.Cover,
                    quote.FinishingServices,
                    quote.ProfitMarginPercent,
                    quote.VolumeDiscounts,
                    quote.SetupCost);

                if (testQuote.PerCopyCost.PriceAfterDiscount <= targetPricePerCopy)
                {
                    return (qty, testQuote.PerCopyCost.PriceAfterDiscount);
                }
            }

            return (-1, 0); // Cannot reach target
        }

        /// <summary>
        /// Compares two different job configurations.
        /// </summary>
        public string CompareConfigurations(
            EnhancedQuotation option1, string name1,
            EnhancedQuotation option2, string name2)
        {
            var sb = new StringBuilder();
            sb.AppendLine("╔══════════════════════════════════════════════════════════════╗");
            sb.AppendLine("║              مقارنة خيارات الطباعة                           ║");
            sb.AppendLine("║           PRINT OPTIONS COMPARISON                           ║");
            sb.AppendLine("╚══════════════════════════════════════════════════════════════╝");
            sb.AppendLine();

            sb.AppendLine($"┌─────────────────────────────┬─────────────────────────────┐");
            sb.AppendLine($"│ {name1,-27} │ {name2,-27} │");
            sb.AppendLine($"├─────────────────────────────┼─────────────────────────────┤");
            sb.AppendLine($"│ الصفحات: {option1.TotalPages,-18} │ الصفحات: {option2.TotalPages,-18} │");
            sb.AppendLine($"│ النسخ: {option1.Quantity,-20} │ النسخ: {option2.Quantity,-20} │");
            sb.AppendLine($"│ الطباعة: {(option1.IsDoubleSided ? "وجهين" : "وجه"),-17} │ الطباعة: {(option2.IsDoubleSided ? "وجهين" : "وجه"),-17} │");
            sb.AppendLine($"├─────────────────────────────┼─────────────────────────────┤");
            sb.AppendLine($"│ سعر النسخة: {option1.PerCopyCost.PriceAfterDiscount,10:F2} EGP   │ سعر النسخة: {option2.PerCopyCost.PriceAfterDiscount,10:F2} EGP   │");
            sb.AppendLine($"│ الإجمالي: {option1.TotalCost.FinalTotal,12:F2} EGP   │ الإجمالي: {option2.TotalCost.FinalTotal,12:F2} EGP   │");
            sb.AppendLine($"└─────────────────────────────┴─────────────────────────────┘");
            sb.AppendLine();

            var diff = Math.Abs(option1.TotalCost.FinalTotal - option2.TotalCost.FinalTotal);
            var cheaper = option1.TotalCost.FinalTotal < option2.TotalCost.FinalTotal ? name1 : name2;
            
            sb.AppendLine($"💰 الفرق: {diff:F2} EGP");
            sb.AppendLine($"✅ الأرخص: {cheaper}");

            return sb.ToString();
        }

        #endregion

        #region Export Methods

        /// <summary>
        /// Exports quotation to a professional HTML invoice.
        /// </summary>
        public string ExportToHtml(EnhancedQuotation quote, string companyName = "أبكس لحلول الطباعة المتكاملة", string companyPhone = "", string companyAddress = "")
        {
            var invoiceNumber = $"QT-{quote.QuotationDate:yyyyMMdd}-{new Random().Next(1000, 9999)}";
            var validUntil    = quote.QuotationDate.AddDays(quote.ValidityDays).ToString("yyyy-MM-dd");
            var printType     = quote.IsDoubleSided ? "وجهين" : "وجه واحد";
            var colorType     = quote.IsColor ? "ألوان" : "أبيض وأسود";

            var sb = new StringBuilder();
            sb.Append(@"<!DOCTYPE html>
<html lang='ar' dir='rtl'>
<head>
<meta charset='UTF-8'>
<meta name='viewport' content='width=device-width, initial-scale=1.0'>
<title>عرض سعر - " + invoiceNumber + @"</title>
<style>
  * { margin: 0; padding: 0; box-sizing: border-box; }
  body {
    font-family: 'Segoe UI', Tahoma, Arial, sans-serif;
    background: #f0f4f8;
    color: #1e293b;
    direction: rtl;
  }
  .page {
    max-width: 850px;
    margin: 30px auto;
    background: #fff;
    border-radius: 12px;
    box-shadow: 0 4px 24px rgba(0,0,0,.12);
    overflow: hidden;
  }
  /* ─── Header ─── */
  .header {
    background: linear-gradient(135deg, #0f172a 0%, #1e293b 100%);
    padding: 36px 40px;
    display: flex;
    justify-content: space-between;
    align-items: flex-start;
  }
  .company-brand .name {
    font-size: 22px;
    font-weight: 700;
    color: #f1f5f9;
    margin-bottom: 6px;
  }
  .company-brand .tagline {
    font-size: 13px;
    color: #94a3b8;
  }
  .invoice-meta { text-align: left; }
  .invoice-meta .label {
    display: block;
    font-size: 11px;
    color: #64748b;
    text-transform: uppercase;
    letter-spacing: .5px;
    margin-bottom: 3px;
  }
  .invoice-meta .invoice-number {
    font-size: 20px;
    font-weight: 700;
    color: #3b82f6;
  }
  .invoice-meta .date {
    font-size: 13px;
    color: #94a3b8;
    margin-top: 4px;
  }
  /* ─── Info Bar ─── */
  .info-bar {
    background: #1e293b;
    padding: 16px 40px;
    display: flex;
    gap: 40px;
  }
  .info-item .info-label { font-size: 11px; color: #64748b; }
  .info-item .info-value { font-size: 13px; color: #e2e8f0; margin-top: 2px; }
  /* ─── Body ─── */
  .body { padding: 36px 40px; }
  /* ─── Section ─── */
  .section-title {
    font-size: 13px;
    font-weight: 600;
    color: #64748b;
    text-transform: uppercase;
    letter-spacing: .6px;
    border-bottom: 2px solid #e2e8f0;
    padding-bottom: 8px;
    margin-bottom: 16px;
    margin-top: 28px;
  }
  .section-title:first-child { margin-top: 0; }
  /* ─── Table ─── */
  table { width: 100%; border-collapse: collapse; margin-bottom: 8px; }
  th {
    background: #f1f5f9;
    color: #475569;
    font-size: 12px;
    font-weight: 600;
    padding: 10px 14px;
    text-align: right;
  }
  td { padding: 10px 14px; font-size: 13px; color: #334155; border-bottom: 1px solid #f1f5f9; }
  tr:last-child td { border-bottom: none; }
  tr:hover td { background: #f8fafc; }
  .col-num { text-align: center; }
  .col-money { text-align: left; font-variant-numeric: tabular-nums; }
  /* ─── Total Row ─── */
  .row-total td {
    background: #eff6ff;
    font-weight: 700;
    color: #1d4ed8;
    font-size: 15px;
    border-top: 2px solid #bfdbfe;
  }
  .row-discount td { color: #dc2626; }
  /* ─── Price Cards ─── */
  .price-cards { display: flex; gap: 16px; margin-top: 24px; }
  .price-card {
    flex: 1;
    border-radius: 10px;
    padding: 20px 24px;
    text-align: center;
  }
  .card-blue { background: linear-gradient(135deg, #3b82f6, #1d4ed8); }
  .card-green { background: linear-gradient(135deg, #10b981, #059669); }
  .price-card .card-label { font-size: 12px; color: rgba(255,255,255,.8); margin-bottom: 8px; }
  .price-card .card-value { font-size: 28px; font-weight: 700; color: #fff; }
  .price-card .card-sub { font-size: 12px; color: rgba(255,255,255,.7); margin-top: 4px; }
  /* ─── Discount Badge ─── */
  .discount-badge {
    display: inline-block;
    background: #fef3c7;
    color: #92400e;
    font-size: 12px;
    font-weight: 600;
    padding: 4px 12px;
    border-radius: 20px;
    margin-top: 12px;
  }
  /* ─── Volume Table ─── */
  .vol-row-best td { background: #f0fdf4; font-weight: 600; }
  .vol-qty { font-weight: 600; color: #2563eb; }
  /* ─── Footer ─── */
  .footer {
    background: #f8fafc;
    border-top: 1px solid #e2e8f0;
    padding: 20px 40px;
    display: flex;
    justify-content: space-between;
    align-items: center;
  }
  .validity { font-size: 12px; color: #64748b; }
  .validity strong { color: #ef4444; }
  .powered { font-size: 11px; color: #94a3b8; }
  @media print {
    body { background: #fff; }
    .page { box-shadow: none; margin: 0; border-radius: 0; }
  }
</style>
</head>
<body>
<div class='page'>

  <!-- Header -->
  <div class='header'>
    <div class='company-brand'>
      <div class='name'>🖨️ " + companyName + @"</div>
      <div class='tagline'>Print OS Professional Suite</div>");

            if (!string.IsNullOrEmpty(companyPhone))
                sb.Append($"<div class='tagline' style='margin-top:4px'>📞 {companyPhone}</div>");
            if (!string.IsNullOrEmpty(companyAddress))
                sb.Append($"<div class='tagline'>📍 {companyAddress}</div>");

            sb.Append($@"
    </div>
    <div class='invoice-meta'>
      <span class='label'>رقم العرض</span>
      <div class='invoice-number'>{invoiceNumber}</div>
      <div class='date'>📅 {quote.QuotationDate:dd/MM/yyyy}</div>
    </div>
  </div>

  <!-- Info Bar -->
  <div class='info-bar'>
    <div class='info-item'>
      <div class='info-label'>العميل</div>
      <div class='info-value'>{(string.IsNullOrEmpty(quote.CustomerName) ? "—" : quote.CustomerName)}</div>
    </div>
    <div class='info-item'>
      <div class='info-label'>عدد الصفحات</div>
      <div class='info-value'>{quote.TotalPages} صفحة</div>
    </div>
    <div class='info-item'>
      <div class='info-label'>الكمية</div>
      <div class='info-value'>{quote.Quantity} نسخة</div>
    </div>
    <div class='info-item'>
      <div class='info-label'>نوع الطباعة</div>
      <div class='info-value'>{printType} • {colorType}</div>
    </div>
  </div>

  <!-- Body -->
  <div class='body'>

    <!-- Job Details -->
    <div class='section-title'>تفاصيل العمل</div>
    <table>
      <tr>
        <th>البيان</th><th class='col-num'>القيمة</th>
      </tr>
      <tr><td>نوع الورق</td><td>{quote.Paper?.Name ?? "—"}</td></tr>
      <tr><td>عدد الأوراق للنسخة الواحدة</td><td class='col-num'>{quote.SheetsPerCopy}</td></tr>
      <tr><td>إجمالي الأوراق</td><td class='col-num'>{quote.TotalSheets}</td></tr>
      <tr><td>الطباعة</td><td>{printType} — {colorType}</td></tr>");

            if (!string.IsNullOrEmpty(quote.JobDescription))
                sb.Append($"<tr><td>وصف العمل</td><td>{quote.JobDescription}</td></tr>");

            sb.Append($@"
    </table>

    <!-- Cost Breakdown per Copy -->
    <div class='section-title'>تكلفة النسخة الواحدة</div>
    <table>
      <tr>
        <th>البند</th><th class='col-money'>التكلفة</th>
      </tr>
      <tr><td>تكلفة الورق</td><td class='col-money'>{quote.PerCopyCost.PaperCost:F2} ج.م</td></tr>
      <tr><td>تكلفة الحبر</td><td class='col-money'>{quote.PerCopyCost.InkCost:F2} ج.م</td></tr>");

            if (quote.PerCopyCost.CoverCost > 0)
                sb.Append($"<tr><td>تكلفة الغلاف</td><td class='col-money'>{quote.PerCopyCost.CoverCost:F2} ج.م</td></tr>");
            if (quote.PerCopyCost.FinishingCost > 0)
                sb.Append($"<tr><td>خدمات التشطيب</td><td class='col-money'>{quote.PerCopyCost.FinishingCost:F2} ج.م</td></tr>");
            if (quote.PerCopyCost.SetupCostShare > 0)
                sb.Append($"<tr><td>تكاليف الإعداد (موزعة)</td><td class='col-money'>{quote.PerCopyCost.SetupCostShare:F2} ج.م</td></tr>");

            sb.Append($@"
      <tr><td>هامش الربح ({quote.ProfitMarginPercent}%)</td><td class='col-money'>{quote.PerCopyCost.ProfitAmount:F2} ج.م</td></tr>
      <tr class='row-total'>
        <td>سعر النسخة الواحدة</td>
        <td class='col-money'>{quote.PerCopyCost.PriceAfterDiscount:F2} ج.م</td>
      </tr>
    </table>

    <!-- Total Summary -->
    <div class='section-title'>الإجمالي النهائي</div>
    <table>
      <tr>
        <th>البند</th><th class='col-money'>المبلغ</th>
      </tr>
      <tr><td>إجمالي التكاليف ({quote.Quantity} نسخة)</td><td class='col-money'>{quote.TotalCost.SubTotal:F2} ج.م</td></tr>
      <tr><td>هامش الربح الإجمالي</td><td class='col-money'>{quote.TotalCost.ProfitAmount:F2} ج.م</td></tr>");

            if (quote.AppliedDiscountPercent > 0)
                sb.Append($"<tr class='row-discount'><td>خصم كمية ({quote.AppliedDiscountPercent:F0}%)</td><td class='col-money'>- {quote.DiscountAmount:F2} ج.م</td></tr>");

            sb.Append($@"
      <tr class='row-total'>
        <td>الإجمالي النهائي</td>
        <td class='col-money'>{quote.TotalCost.FinalTotal:F2} ج.م</td>
      </tr>
    </table>

    <!-- Price Cards -->
    <div class='price-cards'>
      <div class='price-card card-blue'>
        <div class='card-label'>سعر النسخة الواحدة</div>
        <div class='card-value'>{quote.PerCopyCost.PriceAfterDiscount:F2} ج.م</div>
      </div>
      <div class='price-card card-green'>
        <div class='card-label'>الإجمالي لـ {quote.Quantity} نسخة</div>
        <div class='card-value'>{quote.TotalCost.FinalTotal:F2} ج.م</div>");

            if (quote.AppliedDiscountPercent > 0)
                sb.Append($"<div class='card-sub'>وفّرت {quote.DiscountAmount:F2} ج.م 🎉</div>");

            sb.Append(@"
      </div>
    </div>");

            // Volume discount table if multiple tiers
            if (quote.PriceAnalysis != null && quote.PriceAnalysis.Count > 1)
            {
                sb.Append(@"
    <div class='section-title'>جدول خصومات الكمية</div>
    <table>
      <tr>
        <th class='col-num'>الكمية</th>
        <th class='col-money'>سعر النسخة</th>
        <th class='col-money'>الإجمالي</th>
        <th class='col-num'>الخصم</th>
      </tr>");
                foreach (var pt in quote.PriceAnalysis)
                {
                    var isCurrent = pt.Quantity == quote.Quantity;
                    sb.Append($@"
      <tr class='{(isCurrent ? "vol-row-best" : "")}'>
        <td class='col-num vol-qty'>{pt.Quantity}</td>
        <td class='col-money'>{pt.PricePerCopy:F2} ج.م</td>
        <td class='col-money'>{pt.TotalPrice:F2} ج.م</td>
        <td class='col-num'>{(pt.AppliedDiscount > 0 ? pt.AppliedDiscount.ToString("F0") + "%" : "—")}</td>
      </tr>");
                }
                sb.Append("</table>");
            }

            sb.Append($@"
  </div><!-- /body -->

  <!-- Footer -->
  <div class='footer'>
    <div class='validity'>
      ⏳ صالح حتى: <strong>{validUntil}</strong>
    </div>
    <div class='powered'>
      Powered by <strong>Apex Print OS</strong> ·
      Generated {quote.QuotationDate:yyyy-MM-dd HH:mm}
    </div>
  </div>

</div><!-- /page -->
</body></html>");

            return sb.ToString();
        }

        /// <summary>
        /// Exports quotation to JSON format.
        /// </summary>
        public string ExportToJson(EnhancedQuotation quote)
        {
            return System.Text.Json.JsonSerializer.Serialize(quote, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });
        }

        #endregion
    }
}
