using System;
using System.Collections.Generic;
using System.Linq;

namespace Apex.Core.Models
{
    /// <summary>
    /// Represents a print job quotation with full cost breakdown.
    /// </summary>
    public class PrintJobQuotation
    {
        // Input Parameters
        public int TotalPages { get; set; }
        public bool IsDoubleSided { get; set; }
        public int Quantity { get; set; } = 1;
        public decimal PricePerSheet { get; set; }
        
        // Cover
        public bool HasCover { get; set; }
        public decimal CoverPricePerPiece { get; set; }
        
        // Finishing Services
        public List<FinishingService> FinishingServices { get; set; } = new();
        
        // Profit
        public decimal ProfitMarginPercent { get; set; } // e.g., 20 for 20%
        
        // Calculated Values (auto-computed)
        public int SheetsRequired { get; private set; }
        public decimal PrintingCost { get; private set; }
        public decimal CoverCost { get; private set; }
        public decimal FinishingCost { get; private set; }
        public decimal SubTotal { get; private set; }
        public decimal ProfitAmount { get; private set; }
        public decimal FinalPrice { get; private set; }
        
        /// <summary>
        /// Calculates all costs and updates totals.
        /// </summary>
        public void Calculate()
        {
            // Step 1: Calculate sheets required
            CalculateSheetsRequired();
            
            // Step 2: Calculate printing cost
            PrintingCost = SheetsRequired * PricePerSheet * Quantity;
            
            // Step 3: Calculate cover cost
            CoverCost = HasCover ? (CoverPricePerPiece * Quantity) : 0;
            
            // Step 4: Calculate finishing cost
            FinishingCost = FinishingServices.Sum(s => s.PricePerPiece * Quantity);
            
            // Step 5: Calculate subtotal
            SubTotal = PrintingCost + CoverCost + FinishingCost;
            
            // Step 6: Calculate profit
            ProfitAmount = SubTotal * (ProfitMarginPercent / 100);
            
            // Step 7: Calculate final price
            FinalPrice = SubTotal + ProfitAmount;
        }
        
        private void CalculateSheetsRequired()
        {
            if (IsDoubleSided)
            {
                // Double-sided: divide by 2, round up for odd pages
                SheetsRequired = (int)Math.Ceiling(TotalPages / 2.0);
            }
            else
            {
                // Single-sided: 1 sheet per page
                SheetsRequired = TotalPages;
            }
        }
        
        /// <summary>
        /// Gets a detailed cost breakdown.
        /// </summary>
        public string GetBreakdown()
        {
            return $@"
=== Cost Breakdown ===
Pages: {TotalPages} ({(IsDoubleSided ? "Double-Sided" : "Single-Sided")})
Sheets Required: {SheetsRequired}
Quantity: {Quantity}

Printing Cost: {PrintingCost:F2} EGP
Cover Cost: {CoverCost:F2} EGP
Finishing Cost: {FinishingCost:F2} EGP
---
Subtotal: {SubTotal:F2} EGP
Profit ({ProfitMarginPercent}%): {ProfitAmount:F2} EGP
---
TOTAL: {FinalPrice:F2} EGP
";
        }
    }
    
    /// <summary>
    /// Represents a finishing service (سلفان، ريجا، تدبيس، etc.).
    /// </summary>
    public class FinishingService
    {
        public string Name { get; set; } = string.Empty;
        public decimal PricePerPiece { get; set; }
        public string Description { get; set; } = string.Empty;
        
        public FinishingService() { }
        
        public FinishingService(string name, decimal price, string description = "")
        {
            Name = name;
            PricePerPiece = price;
            Description = description;
        }
    }
    
    /// <summary>
    /// Common finishing services catalog.
    /// </summary>
    public static class FinishingServicesCatalog
    {
        public static List<FinishingService> GetCommonServices()
        {
            return new List<FinishingService>
            {
                new FinishingService("سلفان مط (Matt Lamination)", 2.00m, "للغلاف فقط"),
                new FinishingService("سلفان لامع (Gloss Lamination)", 2.50m, "للغلاف فقط"),
                new FinishingService("ريجا (Spot UV)", 3.00m, "تأثير لامع على أجزاء محددة"),
                new FinishingService("بشر/Trim", 0.50m, "قص الحواف"),
                new FinishingService("تدبيس (Stapling)", 0.25m, "تدبيس من الجنب"),
                new FinishingService("لولوة (Spiral Binding)", 5.00m, "تجليد حلزوني"),
                new FinishingService("شرشرة (Perforation)", 1.00m, "خط قطع منقط"),
                new FinishingService("تجليد حراري (Thermal Binding)", 8.00m, "تجليد بالغراء الحراري"),
                new FinishingService("قصات خاصة (Die Cut)", 10.00m, "قص بأشكال مخصصة"),
                new FinishingService("كيس تغليف (Poly Bag)", 0.50m, "كيس بلاستيك شفاف")
            };
        }
    }
}
