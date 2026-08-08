using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Apex.Core.Models
{
    /// <summary>
    /// 📊 Enhanced Quotation Model
    /// Provides detailed per-copy cost breakdown and advanced pricing analysis.
    /// </summary>
    public class EnhancedQuotation
    {
        #region Input Parameters

        /// <summary>
        /// Total number of pages in the document.
        /// </summary>
        public int TotalPages { get; set; }

        /// <summary>
        /// Whether printing is double-sided (duplex).
        /// </summary>
        public bool IsDoubleSided { get; set; }

        /// <summary>
        /// Number of copies to print.
        /// </summary>
        public int Quantity { get; set; } = 1;

        /// <summary>
        /// Paper type and its cost per sheet.
        /// </summary>
        public PaperType Paper { get; set; } = new();

        /// <summary>
        /// Ink/toner cost per page (color or B&W).
        /// </summary>
        public decimal InkCostPerPage { get; set; }

        /// <summary>
        /// Whether printing is in color.
        /// </summary>
        public bool IsColor { get; set; }

        /// <summary>
        /// Cover configuration.
        /// </summary>
        public CoverOptions? Cover { get; set; }

        /// <summary>
        /// List of finishing services.
        /// </summary>
        public List<FinishingService> FinishingServices { get; set; } = new();

        /// <summary>
        /// Profit margin percentage.
        /// </summary>
        public decimal ProfitMarginPercent { get; set; }

        /// <summary>
        /// Volume discount tiers.
        /// </summary>
        public List<VolumeDiscount> VolumeDiscounts { get; set; } = new();

        /// <summary>
        /// Fixed setup/preparation cost.
        /// </summary>
        public decimal SetupCost { get; set; }

        /// <summary>
        /// Customer name for the quotation.
        /// </summary>
        public string CustomerName { get; set; } = "";

        /// <summary>
        /// Job description.
        /// </summary>
        public string JobDescription { get; set; } = "";

        /// <summary>
        /// Quotation date.
        /// </summary>
        public DateTime QuotationDate { get; set; } = DateTime.Now;

        /// <summary>
        /// Validity period in days.
        /// </summary>
        public int ValidityDays { get; set; } = 7;

        #endregion

        #region Calculated Values

        /// <summary>
        /// Sheets required per copy.
        /// </summary>
        public int SheetsPerCopy { get; private set; }

        /// <summary>
        /// Total sheets required for all copies.
        /// </summary>
        public int TotalSheets { get; private set; }

        /// <summary>
        /// Cost breakdown per single copy.
        /// </summary>
        public CopyCostBreakdown PerCopyCost { get; private set; } = new();

        /// <summary>
        /// Total cost breakdown for all copies.
        /// </summary>
        public TotalCostBreakdown TotalCost { get; private set; } = new();

        /// <summary>
        /// Applied discount percentage.
        /// </summary>
        public decimal AppliedDiscountPercent { get; private set; }

        /// <summary>
        /// Discount amount.
        /// </summary>
        public decimal DiscountAmount { get; private set; }

        /// <summary>
        /// Price analysis for different quantities.
        /// </summary>
        public List<QuantityPricePoint> PriceAnalysis { get; private set; } = new();

        #endregion

        #region Calculation Methods

        /// <summary>
        /// Calculates all costs and updates the quotation.
        /// </summary>
        public void Calculate()
        {
            CalculateCore();

            // Step 5: Generate price analysis (only for main calculation, not for clones)
            GeneratePriceAnalysis();
        }

        /// <summary>
        /// Core calculation without price analysis (to prevent infinite recursion).
        /// </summary>
        private void CalculateCore()
        {
            // Step 1: Calculate sheets per copy
            SheetsPerCopy = IsDoubleSided
                ? (int)Math.Ceiling(TotalPages / 2.0)
                : TotalPages;

            TotalSheets = SheetsPerCopy * Quantity;

            // Step 2: Calculate per-copy costs
            CalculatePerCopyCosts();

            // Step 3: Calculate total costs
            CalculateTotalCosts();

            // Step 4: Apply volume discounts
            ApplyVolumeDiscounts();
        }

        private void CalculatePerCopyCosts()
        {
            PerCopyCost = new CopyCostBreakdown
            {
                // Paper cost per copy
                PaperCost = SheetsPerCopy * Paper.PricePerSheet,

                // Ink cost per copy
                InkCost = TotalPages * InkCostPerPage,

                // Cover cost per copy
                CoverCost = Cover?.TotalCostPerPiece ?? 0,

                // Finishing cost per copy
                FinishingCost = FinishingServices.Sum(s => s.PricePerPiece),

                // Setup cost distributed per copy (when quantity = 1, or proportional)
                SetupCostShare = Quantity > 0 ? SetupCost / Quantity : SetupCost
            };

            // Calculate subtotal per copy
            PerCopyCost.SubTotal =
                PerCopyCost.PaperCost +
                PerCopyCost.InkCost +
                PerCopyCost.CoverCost +
                PerCopyCost.FinishingCost +
                PerCopyCost.SetupCostShare;

            // Profit per copy
            PerCopyCost.ProfitAmount = PerCopyCost.SubTotal * (ProfitMarginPercent / 100);

            // Final price per copy (before discount)
            PerCopyCost.TotalPrice = PerCopyCost.SubTotal + PerCopyCost.ProfitAmount;
        }

        private void CalculateTotalCosts()
        {
            TotalCost = new TotalCostBreakdown
            {
                PaperCost = PerCopyCost.PaperCost * Quantity,
                InkCost = PerCopyCost.InkCost * Quantity,
                CoverCost = PerCopyCost.CoverCost * Quantity,
                FinishingCost = PerCopyCost.FinishingCost * Quantity,
                SetupCost = SetupCost, // Setup is fixed, not multiplied
                Quantity = Quantity
            };

            // Subtotal before profit
            TotalCost.SubTotal =
                TotalCost.PaperCost +
                TotalCost.InkCost +
                TotalCost.CoverCost +
                TotalCost.FinishingCost +
                TotalCost.SetupCost;

            // Profit
            TotalCost.ProfitAmount = TotalCost.SubTotal * (ProfitMarginPercent / 100);

            // Total before discount
            TotalCost.TotalBeforeDiscount = TotalCost.SubTotal + TotalCost.ProfitAmount;
        }

        private void ApplyVolumeDiscounts()
        {
            // Find applicable discount tier
            var applicableDiscount = VolumeDiscounts
                .Where(d => Quantity >= d.MinQuantity)
                .OrderByDescending(d => d.MinQuantity)
                .FirstOrDefault();

            if (applicableDiscount != null)
            {
                AppliedDiscountPercent = applicableDiscount.DiscountPercent;
                DiscountAmount = TotalCost.TotalBeforeDiscount * (AppliedDiscountPercent / 100);
            }
            else
            {
                AppliedDiscountPercent = 0;
                DiscountAmount = 0;
            }

            // Final total
            TotalCost.DiscountAmount = DiscountAmount;
            TotalCost.FinalTotal = TotalCost.TotalBeforeDiscount - DiscountAmount;

            // Update per-copy price after discount
            PerCopyCost.PriceAfterDiscount = Quantity > 0
                ? TotalCost.FinalTotal / Quantity
                : PerCopyCost.TotalPrice;
        }

        private void GeneratePriceAnalysis()
        {
            PriceAnalysis.Clear();

            // Generate price points for different quantities
            var quantities = new[] { 1, 5, 10, 25, 50, 100, 250, 500, 1000 };

            decimal? singleCopyPrice = null;

            foreach (var qty in quantities.Where(q => q >= 1))
            {
                // Create temp quotation for this quantity
                var tempQuote = CloneWithQuantity(qty);
                // Use CalculateCore to avoid infinite recursion!
                tempQuote.CalculateCore();

                if (singleCopyPrice == null)
                {
                    singleCopyPrice = tempQuote.PerCopyCost.PriceAfterDiscount;
                }

                PriceAnalysis.Add(new QuantityPricePoint
                {
                    Quantity = qty,
                    TotalPrice = tempQuote.TotalCost.FinalTotal,
                    PricePerCopy = tempQuote.PerCopyCost.PriceAfterDiscount,
                    AppliedDiscount = tempQuote.AppliedDiscountPercent,
                    SavingsVsSingle = qty > 1
                        ? singleCopyPrice.Value - tempQuote.PerCopyCost.PriceAfterDiscount
                        : 0
                });
            }
        }

        private EnhancedQuotation CloneWithQuantity(int newQuantity)
        {
            return new EnhancedQuotation
            {
                TotalPages = TotalPages,
                IsDoubleSided = IsDoubleSided,
                Quantity = newQuantity,
                Paper = Paper,
                InkCostPerPage = InkCostPerPage,
                IsColor = IsColor,
                Cover = Cover,
                FinishingServices = FinishingServices,
                ProfitMarginPercent = ProfitMarginPercent,
                VolumeDiscounts = VolumeDiscounts,
                SetupCost = SetupCost
            };
        }

        #endregion

        #region Export Methods

        /// <summary>
        /// Gets a detailed breakdown for display or printing.
        /// </summary>
        public string GetDetailedBreakdown()
        {
            var sb = new StringBuilder();

            sb.AppendLine("╔══════════════════════════════════════════════════════════════╗");
            sb.AppendLine("║                    عرض سعر تفصيلي                            ║");
            sb.AppendLine("║                 DETAILED QUOTATION                           ║");
            sb.AppendLine("╚══════════════════════════════════════════════════════════════╝");
            sb.AppendLine();

            // Header
            if (!string.IsNullOrEmpty(CustomerName))
                sb.AppendLine($"  العميل / Customer: {CustomerName}");
            if (!string.IsNullOrEmpty(JobDescription))
                sb.AppendLine($"  الوصف / Description: {JobDescription}");
            sb.AppendLine($"  التاريخ / Date: {QuotationDate:yyyy-MM-dd}");
            sb.AppendLine($"  صالح حتى / Valid Until: {QuotationDate.AddDays(ValidityDays):yyyy-MM-dd}");
            sb.AppendLine();

            // Job Details
            sb.AppendLine("┌──────────────────────────────────────────────────────────────┐");
            sb.AppendLine("│  تفاصيل العمل / Job Details                                  │");
            sb.AppendLine("├──────────────────────────────────────────────────────────────┤");
            sb.AppendLine($"│  عدد الصفحات / Pages: {TotalPages}");
            sb.AppendLine($"│  نوع الطباعة / Print Type: {(IsDoubleSided ? "وجهين / Double-Sided" : "وجه واحد / Single-Sided")}");
            sb.AppendLine($"│  الألوان / Color: {(IsColor ? "ملون / Color" : "أبيض وأسود / B&W")}");
            sb.AppendLine($"│  عدد الأوراق للنسخة / Sheets per Copy: {SheetsPerCopy}");
            sb.AppendLine($"│  نوع الورق / Paper: {Paper.Name}");
            sb.AppendLine($"│  عدد النسخ / Quantity: {Quantity}");
            sb.AppendLine("└──────────────────────────────────────────────────────────────┘");
            sb.AppendLine();

            // Per-Copy Breakdown
            sb.AppendLine("┌──────────────────────────────────────────────────────────────┐");
            sb.AppendLine("│  💰 تكلفة النسخة الواحدة / COST PER COPY                     │");
            sb.AppendLine("├──────────────────────────────────────────────────────────────┤");
            sb.AppendLine($"│  الورق / Paper:           {PerCopyCost.PaperCost,15:F2} EGP  │");
            sb.AppendLine($"│  الحبر / Ink:             {PerCopyCost.InkCost,15:F2} EGP  │");
            if (PerCopyCost.CoverCost > 0)
                sb.AppendLine($"│  الغلاف / Cover:          {PerCopyCost.CoverCost,15:F2} EGP  │");
            if (PerCopyCost.FinishingCost > 0)
                sb.AppendLine($"│  التشطيب / Finishing:     {PerCopyCost.FinishingCost,15:F2} EGP  │");
            if (PerCopyCost.SetupCostShare > 0)
                sb.AppendLine($"│  الإعداد / Setup:         {PerCopyCost.SetupCostShare,15:F2} EGP  │");
            sb.AppendLine("├──────────────────────────────────────────────────────────────┤");
            sb.AppendLine($"│  المجموع / Subtotal:      {PerCopyCost.SubTotal,15:F2} EGP  │");
            sb.AppendLine($"│  الربح ({ProfitMarginPercent}%):              {PerCopyCost.ProfitAmount,15:F2} EGP  │");
            sb.AppendLine("├──────────────────────────────────────────────────────────────┤");
            sb.AppendLine($"│  ★ سعر النسخة / Price per Copy:  {PerCopyCost.TotalPrice,12:F2} EGP ★│");
            if (AppliedDiscountPercent > 0)
                sb.AppendLine($"│  ★ بعد الخصم / After Discount:   {PerCopyCost.PriceAfterDiscount,12:F2} EGP ★│");
            sb.AppendLine("└──────────────────────────────────────────────────────────────┘");
            sb.AppendLine();

            // Total Breakdown
            sb.AppendLine("┌──────────────────────────────────────────────────────────────┐");
            sb.AppendLine($"│  📦 الإجمالي لـ {Quantity} نسخة / TOTAL FOR {Quantity} COPIES              │");
            sb.AppendLine("├──────────────────────────────────────────────────────────────┤");
            sb.AppendLine($"│  الورق / Paper:           {TotalCost.PaperCost,15:F2} EGP  │");
            sb.AppendLine($"│  الحبر / Ink:             {TotalCost.InkCost,15:F2} EGP  │");
            if (TotalCost.CoverCost > 0)
                sb.AppendLine($"│  الغلاف / Cover:          {TotalCost.CoverCost,15:F2} EGP  │");
            if (TotalCost.FinishingCost > 0)
                sb.AppendLine($"│  التشطيب / Finishing:     {TotalCost.FinishingCost,15:F2} EGP  │");
            if (TotalCost.SetupCost > 0)
                sb.AppendLine($"│  الإعداد / Setup:         {TotalCost.SetupCost,15:F2} EGP  │");
            sb.AppendLine("├──────────────────────────────────────────────────────────────┤");
            sb.AppendLine($"│  المجموع / Subtotal:      {TotalCost.SubTotal,15:F2} EGP  │");
            sb.AppendLine($"│  الربح / Profit:          {TotalCost.ProfitAmount,15:F2} EGP  │");
            if (AppliedDiscountPercent > 0)
            {
                sb.AppendLine($"│  قبل الخصم / Before Disc: {TotalCost.TotalBeforeDiscount,15:F2} EGP  │");
                sb.AppendLine($"│  الخصم ({AppliedDiscountPercent}%):            {-DiscountAmount,15:F2} EGP  │");
            }
            sb.AppendLine("╔══════════════════════════════════════════════════════════════╗");
            sb.AppendLine($"║  ★★★ الإجمالي النهائي / FINAL TOTAL: {TotalCost.FinalTotal,12:F2} EGP ★★★ ║");
            sb.AppendLine("╚══════════════════════════════════════════════════════════════╝");
            sb.AppendLine();

            // Price Analysis Table
            if (PriceAnalysis.Count > 1)
            {
                sb.AppendLine("┌──────────────────────────────────────────────────────────────┐");
                sb.AppendLine("│  📊 تحليل الأسعار حسب الكمية / QUANTITY PRICE ANALYSIS       │");
                sb.AppendLine("├──────────┬─────────────┬─────────────┬────────────┬─────────┤");
                sb.AppendLine("│ الكمية   │ سعر النسخة  │ الإجمالي    │ الخصم %    │ التوفير │");
                sb.AppendLine("│ Qty      │ Per Copy    │ Total       │ Discount   │ Savings │");
                sb.AppendLine("├──────────┼─────────────┼─────────────┼────────────┼─────────┤");

                foreach (var point in PriceAnalysis)
                {
                    var marker = point.Quantity == Quantity ? "→ " : "  ";
                    sb.AppendLine($"│{marker}{point.Quantity,6}   │ {point.PricePerCopy,9:F2}   │ {point.TotalPrice,9:F2}   │ {point.AppliedDiscount,8:F1}%  │ {point.SavingsVsSingle,6:F2} │");
                }

                sb.AppendLine("└──────────┴─────────────┴─────────────┴────────────┴─────────┘");
            }

            sb.AppendLine();
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine("  Generated by Apex Print OS | عرض السعر من نظام Apex");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

            return sb.ToString();
        }

        /// <summary>
        /// Gets a simple summary for quick display.
        /// </summary>
        public string GetQuickSummary()
        {
            return $@"
┌─────────────────────────────────────┐
│  {TotalPages} صفحة × {Quantity} نسخة
│  سعر النسخة: {PerCopyCost.PriceAfterDiscount:F2} EGP
│  الإجمالي: {TotalCost.FinalTotal:F2} EGP
│  {(AppliedDiscountPercent > 0 ? $"خصم {AppliedDiscountPercent}% مطبق" : "")}
└─────────────────────────────────────┘";
        }

        #endregion
    }

    #region Supporting Types

    /// <summary>
    /// Cost breakdown for a single copy.
    /// </summary>
    public class CopyCostBreakdown
    {
        public decimal PaperCost { get; set; }
        public decimal InkCost { get; set; }
        public decimal CoverCost { get; set; }
        public decimal FinishingCost { get; set; }
        public decimal SetupCostShare { get; set; }
        public decimal SubTotal { get; set; }
        public decimal ProfitAmount { get; set; }
        public decimal TotalPrice { get; set; }
        public decimal PriceAfterDiscount { get; set; }
    }

    /// <summary>
    /// Total cost breakdown for all copies.
    /// </summary>
    public class TotalCostBreakdown
    {
        public int Quantity { get; set; }
        public decimal PaperCost { get; set; }
        public decimal InkCost { get; set; }
        public decimal CoverCost { get; set; }
        public decimal FinishingCost { get; set; }
        public decimal SetupCost { get; set; }
        public decimal SubTotal { get; set; }
        public decimal ProfitAmount { get; set; }
        public decimal TotalBeforeDiscount { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal FinalTotal { get; set; }
    }

    /// <summary>
    /// Paper type with pricing.
    /// </summary>
    public class PaperType
    {
        public string Name { get; set; } = "A4 80gsm";
        public string Size { get; set; } = "A4";
        public int Gsm { get; set; } = 80;
        public decimal PricePerSheet { get; set; } = 0.10m;

        public PaperType() { }

        public PaperType(string name, decimal pricePerSheet)
        {
            Name = name;
            PricePerSheet = pricePerSheet;
        }
    }

    /// <summary>
    /// Cover options and pricing.
    /// </summary>
    public class CoverOptions
    {
        public string CoverType { get; set; } = "Cardboard 300gsm";
        public decimal CoverPrintCost { get; set; }
        public decimal CoverMaterialCost { get; set; }
        public bool HasLamination { get; set; }
        public decimal LaminationCost { get; set; }

        public decimal TotalCostPerPiece =>
            CoverPrintCost + CoverMaterialCost + (HasLamination ? LaminationCost : 0);
    }

    /// <summary>
    /// Volume discount tier.
    /// </summary>
    public class VolumeDiscount
    {
        public int MinQuantity { get; set; }
        public decimal DiscountPercent { get; set; }
        public string Description { get; set; } = "";

        public VolumeDiscount() { }

        public VolumeDiscount(int minQty, decimal discount, string description = "")
        {
            MinQuantity = minQty;
            DiscountPercent = discount;
            Description = description;
        }
    }

    /// <summary>
    /// Price point for quantity analysis.
    /// </summary>
    public class QuantityPricePoint
    {
        public int Quantity { get; set; }
        public decimal TotalPrice { get; set; }
        public decimal PricePerCopy { get; set; }
        public decimal AppliedDiscount { get; set; }
        public decimal SavingsVsSingle { get; set; }
    }

    /// <summary>
    /// Common paper types catalog.
    /// </summary>
    public static class PaperTypesCatalog
    {
        public static List<PaperType> GetCommonPaperTypes()
        {
            return new List<PaperType>
            {
                new PaperType { Name = "A4 عادي 80gsm", Size = "A4", Gsm = 80, PricePerSheet = 0.10m },
                new PaperType { Name = "A4 فاخر 100gsm", Size = "A4", Gsm = 100, PricePerSheet = 0.15m },
                new PaperType { Name = "A4 كوشيه 135gsm", Size = "A4", Gsm = 135, PricePerSheet = 0.25m },
                new PaperType { Name = "A4 كوشيه 200gsm", Size = "A4", Gsm = 200, PricePerSheet = 0.40m },
                new PaperType { Name = "A3 عادي 80gsm", Size = "A3", Gsm = 80, PricePerSheet = 0.20m },
                new PaperType { Name = "A3 كوشيه 135gsm", Size = "A3", Gsm = 135, PricePerSheet = 0.50m },
                new PaperType { Name = "كرتون 300gsm للأغلفة", Size = "A4", Gsm = 300, PricePerSheet = 1.00m },
                new PaperType { Name = "ورق لاصق", Size = "A4", Gsm = 0, PricePerSheet = 2.00m },
            };
        }
    }

    /// <summary>
    /// Default volume discounts.
    /// </summary>
    public static class DefaultVolumeDiscounts
    {
        public static List<VolumeDiscount> GetStandardTiers()
        {
            return new List<VolumeDiscount>
            {
                new VolumeDiscount(10, 5, "خصم 5% للـ 10 نسخ أو أكثر"),
                new VolumeDiscount(25, 10, "خصم 10% للـ 25 نسخة أو أكثر"),
                new VolumeDiscount(50, 15, "خصم 15% للـ 50 نسخة أو أكثر"),
                new VolumeDiscount(100, 20, "خصم 20% للـ 100 نسخة أو أكثر"),
                new VolumeDiscount(250, 25, "خصم 25% للـ 250 نسخة أو أكثر"),
                new VolumeDiscount(500, 30, "خصم 30% للـ 500 نسخة أو أكثر"),
            };
        }
    }

    #endregion
}
