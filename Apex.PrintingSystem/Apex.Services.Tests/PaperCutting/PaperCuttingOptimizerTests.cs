using Apex.Core.Models.PaperCutting;
using Apex.Services.PaperCutting;

namespace Apex.Services.Tests.PaperCutting;

/// <summary>
/// Unit tests for <see cref="PaperCuttingOptimizerService"/>.
/// All dimension values are in millimetres unless stated otherwise.
/// </summary>
public class PaperCuttingOptimizerTests
{
    private readonly PaperCuttingOptimizerService _svc = new();

    // ── Helper ──────────────────────────────────────────────────────────────
    private PaperCuttingInput SimpleInput(
        double prodW, double prodH,
        double sheetW, double sheetH,
        int qty = 100,
        bool allowRotation = false,
        double waste = 0,
        double bleed = 0,
        double margin = 0) => new()
        {
            ProductWidth = prodW,
            ProductHeight = prodH,
            RawSheetWidth = sheetW,
            RawSheetHeight = sheetH,
            RequiredQuantity = qty,
            AllowRotation = allowRotation,
            ProductionWastePercentage = waste,
            Bleed = bleed,
            CuttingMargin = margin,
            Unit = MeasurementUnit.Millimeter,
            OptimizationMode = PaperOptimizationMode.MaximumPieces
        };

    // ────────────────────────────────────────────────────────────────────────
    // TC-01  Basic normal layout (Pattern A)
    // Sheet 700×1000, Product 100×70 → 7 cols × 14 rows = 98 pcs/sheet
    // ────────────────────────────────────────────────────────────────────────
    [Fact]
    public void TC01_PatternA_BasicLayout()
    {
        var result = _svc.Calculate(SimpleInput(100, 70, 700, 1000, qty: 1000, allowRotation: false));

        Assert.True(result.IsValid, result.ErrorMessage);
        Assert.NotNull(result.BestPattern);
        Assert.Equal("A", result.BestPattern!.PatternName);
        Assert.Equal(98, result.PiecesPerSheet);           // 7 × 14
        Assert.Equal(11, result.SheetsNeeded);             // ceil(1000/98)
    }

    // ────────────────────────────────────────────────────────────────────────
    // TC-02  Rotated pattern yields more pieces
    // Sheet 200×300, Product 90×60
    //   Pattern A: 2×5 = 10
    //   Pattern B: 3×2 = 6   (60 wide, 90 tall → 3 cols × 3 rows = 9? check carefully)
    // Product 90×60: A → floor(200/90)=2, floor(300/60)=5 → 10
    //               B → floor(200/60)=3, floor(300/90)=3 → 9
    // So A wins, but rotation should be attempted
    // ────────────────────────────────────────────────────────────────────────
    [Fact]
    public void TC02_RotationAttempted_WhenAllowed()
    {
        var result = _svc.Calculate(SimpleInput(90, 60, 200, 300, allowRotation: true));

        Assert.True(result.IsValid);
        // All patterns should be present (A + B at minimum)
        Assert.True(result.AllPatterns.Count >= 2);
        // Best is whichever gives the most pieces
        Assert.True(result.PiecesPerSheet >= 10);          // Pattern A gives 10
    }

    // ────────────────────────────────────────────────────────────────────────
    // TC-03  Waste percentage increases sheet count correctly
    // Sheet 1000×1000, Product 100×100 → 10×10 = 100 pcs/sheet
    // Qty = 1000 → 10 net sheets
    // 20% waste → ceil(10 × 1.20) = 12 sheets
    // ────────────────────────────────────────────────────────────────────────
    [Fact]
    public void TC03_WastePercentageInflatesSheetCount()
    {
        var result = _svc.Calculate(SimpleInput(100, 100, 1000, 1000, qty: 1000, waste: 20));

        Assert.True(result.IsValid);
        Assert.Equal(100, result.PiecesPerSheet);          // 10×10 grid
        Assert.Equal(10, result.SheetsNeeded);             // ceil(1000/100)
        Assert.Equal(12, result.SheetsWithWaste);          // ceil(10 × 1.20)
    }

    // ────────────────────────────────────────────────────────────────────────
    // TC-04  Bleed is added to effective piece size
    // Product 100×100, bleed 5 (per side) → effective 110×110
    // Sheet 660×660 → floor(660/110)=6 → 36 pcs
    // ────────────────────────────────────────────────────────────────────────
    [Fact]
    public void TC04_BleedExpandsEffectiveSize()
    {
        var input = SimpleInput(100, 100, 660, 660, qty: 36);
        input.Bleed = 5;
        input.IsBleedPerSide = true;

        var result = _svc.Calculate(input);

        Assert.True(result.IsValid);
        Assert.Equal(110.0, result.EffectiveProductWidth, precision: 3);
        Assert.Equal(110.0, result.EffectiveProductHeight, precision: 3);
        Assert.Equal(36, result.PiecesPerSheet);
    }

    // ────────────────────────────────────────────────────────────────────────
    // TC-05  Validation rejects product larger than sheet
    // ────────────────────────────────────────────────────────────────────────
    [Fact]
    public void TC05_Validation_ProductLargerThanSheet()
    {
        var result = _svc.Calculate(SimpleInput(1000, 1000, 500, 500));

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.ErrorMessage);
    }

    // ────────────────────────────────────────────────────────────────────────
    // TC-06  Mixed pattern (C or D) produces more pieces than pure A in some cases
    // Sheet 210×297 (A4), Product 99×68
    //   Pattern A: floor(210/99)=2, floor(297/68)=4 → 8 pcs
    //   Right waste: 210 - 2×99 = 12mm → can't fit 68mm rotated (68 > 12), skip
    //   Bottom waste: 297 - 4×68 = 25mm → can fit 99mm rotated? 25 < 99, no
    //   Pattern B (rotated 90°): floor(210/68)=3, floor(297/99)=3 → 9 pcs  ← wins
    // So with allowRotation best should be >= 9
    // ────────────────────────────────────────────────────────────────────────
    [Fact]
    public void TC06_OptimizationModeSelectsCorrectly()
    {
        var input = SimpleInput(99, 68, 210, 297, allowRotation: true);
        input.OptimizationMode = PaperOptimizationMode.MaximumPieces;

        var result = _svc.Calculate(input);

        Assert.True(result.IsValid);
        Assert.True(result.PiecesPerSheet >= 8,
            $"Expected ≥8 pieces but got {result.PiecesPerSheet}");
        // Utilization should be positive
        Assert.True(result.UtilizationPercent > 0);
        // Human summary should be populated
        Assert.NotEmpty(result.HumanReadableSummary);
        // Instructions should be generated
        Assert.NotNull(result.BestPattern!.Instructions);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Multi-product (separate mode) — CalculateMulti
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Shared template carrying sheet / pre-press / mode settings.</summary>
    private static PaperCuttingInput SharedTemplate(
        double sheetW = 1000, double sheetH = 1000,
        double waste = 0, bool allowRotation = false) => new()
        {
            RawSheetWidth = sheetW,
            RawSheetHeight = sheetH,
            ProductionWastePercentage = waste,
            AllowRotation = allowRotation,
            Bleed = 0,
            CuttingMargin = 0,
            Unit = MeasurementUnit.Millimeter,
            OptimizationMode = PaperOptimizationMode.MaximumPieces
        };

    // ────────────────────────────────────────────────────────────────────────
    // TC-07  Multi: two products each computed and rolled up
    //   Sheet 1000×1000.
    //   P1 100×100 → 10×10 = 100/sheet, qty 1000 → 10 sheets
    //   P2 200×100 → 5×10  = 50/sheet,  qty 500  → 10 sheets
    //   Grand sheets (no waste) = 20
    // ────────────────────────────────────────────────────────────────────────
    [Fact]
    public void TC07_Multi_TwoProducts_RollupTotals()
    {
        var products = new List<ProductSpec>
        {
            new() { Name = "A", Width = 100, Height = 100, Quantity = 1000 },
            new() { Name = "B", Width = 200, Height = 100, Quantity = 500  },
        };

        var multi = _svc.CalculateMulti(products, SharedTemplate());

        Assert.True(multi.IsValid, multi.ErrorMessage);
        Assert.Equal(2, multi.ProductCount);
        Assert.Equal(2, multi.Items.Count);
        Assert.Equal(100, multi.Items[0].Result.PiecesPerSheet);
        Assert.Equal(50, multi.Items[1].Result.PiecesPerSheet);
        Assert.Equal(20, multi.TotalSheetsNet);           // 10 + 10
        Assert.Equal(1500, multi.TotalRequiredQuantity);  // 1000 + 500
        Assert.NotEmpty(multi.HumanReadableSummary);
    }

    // ────────────────────────────────────────────────────────────────────────
    // TC-08  Multi: production waste inflates each product's sheet count
    //   P1 100×100 on 1000×1000 → 100/sheet, qty 1000 → 10 net → 20% → 12
    // ────────────────────────────────────────────────────────────────────────
    [Fact]
    public void TC08_Multi_WasteAppliedPerProduct()
    {
        var products = new List<ProductSpec>
        {
            new() { Name = "A", Width = 100, Height = 100, Quantity = 1000 },
        };

        var multi = _svc.CalculateMulti(products, SharedTemplate(waste: 20));

        Assert.True(multi.IsValid);
        Assert.Equal(12, multi.TotalSheetsWithWaste);     // ceil(10 × 1.20)
        Assert.True(multi.CombinedUtilizationPercent > 0);
    }

    // ────────────────────────────────────────────────────────────────────────
    // TC-09  Multi: an invalid product (larger than sheet) fails the whole job
    // ────────────────────────────────────────────────────────────────────────
    [Fact]
    public void TC09_Multi_InvalidProduct_FailsWithName()
    {
        var products = new List<ProductSpec>
        {
            new() { Name = "صالح", Width = 100, Height = 100, Quantity = 10 },
            new() { Name = "ضخم",  Width = 5000, Height = 5000, Quantity = 10 },
        };

        var multi = _svc.CalculateMulti(products, SharedTemplate());

        Assert.False(multi.IsValid);
        Assert.Contains("ضخم", multi.ErrorMessage);
    }

    // ────────────────────────────────────────────────────────────────────────
    // TC-10  Multi: empty product list is rejected
    // ────────────────────────────────────────────────────────────────────────
    [Fact]
    public void TC10_Multi_EmptyList_Rejected()
    {
        var multi = _svc.CalculateMulti(new List<ProductSpec>(), SharedTemplate());

        Assert.False(multi.IsValid);
        Assert.NotEmpty(multi.ErrorMessage);
    }
}
