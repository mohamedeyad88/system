using Apex.Core.Models.PaperCutting;
using Apex.Services.PaperCutting;

namespace Apex.Services.Tests.PaperCutting;

/// <summary>Real-scenario yield calculation for the paper-cutting optimizer.</summary>
public class PaperCuttingRealTests
{
    [Fact]
    public void A5Flyers_From70x100Sheet_ComputesReasonableYield()
    {
        var input = new PaperCuttingInput
        {
            ProductWidth = 148, ProductHeight = 210,   // A5 flyer
            RawSheetWidth = 700, RawSheetHeight = 1000, // 70×100 cm parent
            RequiredQuantity = 5000,
            ProductionWastePercentage = 5,
            Bleed = 3, CuttingMargin = 3,
            AllowRotation = true,
            OptimizationMode = PaperOptimizationMode.MaximumPieces,
        };

        PaperCuttingResult res = new PaperCuttingOptimizerService().Calculate(input);

        Assert.True(res.IsValid, res.ErrorMessage);
        Assert.True(res.PiecesPerSheet >= 12, $"only {res.PiecesPerSheet}/sheet");
        Assert.True(res.SheetsNeeded * res.PiecesPerSheet >= 5000);
        Assert.InRange(res.UtilizationPercent, 50, 100);

        Directory.CreateDirectory(@"D:\Apex\publish");
        File.WriteAllText(@"D:\Apex\publish\paper-cutting-result.txt",
            $"A5 flyer (148×210) from 70×100cm, qty 5000, 5% waste:\n" +
            $"  pieces/sheet = {res.PiecesPerSheet}\n" +
            $"  sheets needed = {res.SheetsNeeded} (+waste = {res.SheetsWithWaste})\n" +
            $"  total produced = {res.TotalPiecesProduced} (extra {res.ExtraPieces})\n" +
            $"  utilization = {res.UtilizationPercent:F1}%\n" +
            $"  {res.HumanReadableSummary}\n");
    }
}
