using Apex.Core.Models.Imposition;
using Apex.Services.Imposition;
using Apex.Services.PaperCutting;

namespace Apex.Services.Tests.Imposition;

/// <summary>
/// Bleed that differs per axis has to survive all the way to the trim geometry.
///
/// The plan carried a single bleed figure while pages were placed using separate
/// horizontal/vertical bleeds. Everything downstream that insets by bleed — the
/// TrimBox a cutter reads, and the crop marks an operator cuts to — then used the
/// wrong figure on one axis. On a 8mm/2mm job the trim line lands 6mm inside the
/// artwork on every product of the sheet.
/// </summary>
public class AsymmetricBleedTests
{
    private static ImpositionService NewService() => new(new PaperCuttingOptimizerService());

    private static ImpositionInput Job(double bleed, double? bh, double? bv) => new()
    {
        Type = ImpositionType.NUp,
        NUp = NUpLayout.TwoUp,
        SourcePageCount = 2,
        PageWidth = 100,
        PageHeight = 140,
        SheetWidth = 450,
        SheetHeight = 320,
        Bleed = bleed,
        BleedHorizontal = bh,
        BleedVertical = bv,
        SheetMargin = 5,
        Gutter = 0,
        AllowRotation = false,
        Alignment = SheetAlignment.TopLeft,
    };

    [Fact]
    public void PerAxisBleed_ReachesThePlan()
    {
        var plan = NewService().Plan(Job(bleed: 3, bh: 8, bv: 2));

        Assert.True(plan.IsValid, plan.ErrorMessage);
        Assert.Equal(8, plan.BleedHorizontalMm, 3);
        Assert.Equal(2, plan.BleedVerticalMm, 3);
    }

    [Fact]
    public void PerAxisBleed_DrivesThePlacedPageSize()
    {
        var plan = NewService().Plan(Job(bleed: 3, bh: 8, bv: 2));

        // 100 + 2×8 wide, 140 + 2×2 tall — the placed size the slots actually use.
        Assert.True(plan.IsValid, plan.ErrorMessage);
        Assert.Equal(116, plan.PlacedPageWidthMm, 3);
        Assert.Equal(144, plan.PlacedPageHeightMm, 3);
    }

    [Fact]
    public void TheReportedBleedMatchesWhatWasPlaced()
    {
        // The whole point: whatever inflated the slot must be what a consumer
        // insets by to recover the trim line.
        var plan = NewService().Plan(Job(bleed: 3, bh: 8, bv: 2));
        var slot = plan.Sheets[0].Front.Slots.First(s => !s.IsBlank);

        Assert.Equal(100, slot.Width - 2 * plan.BleedHorizontalMm, 3);
        Assert.Equal(140, slot.Height - 2 * plan.BleedVerticalMm, 3);
    }

    [Fact]
    public void WithoutOverrides_BothAxesFallBackToTheSingleBleed()
    {
        var plan = NewService().Plan(Job(bleed: 5, bh: null, bv: null));

        Assert.True(plan.IsValid, plan.ErrorMessage);
        Assert.Equal(5, plan.BleedHorizontalMm, 3);
        Assert.Equal(5, plan.BleedVerticalMm, 3);
        Assert.Equal(plan.BleedMm, plan.BleedHorizontalMm, 3);
    }

    [Fact]
    public void ZeroBleed_StaysZero_NotTreatedAsUnset()
    {
        // 0 is a real choice (no bleed), and must not silently fall back to Bleed.
        var plan = NewService().Plan(Job(bleed: 4, bh: 0, bv: 0));

        Assert.True(plan.IsValid, plan.ErrorMessage);
        Assert.Equal(0, plan.BleedHorizontalMm, 3);
        Assert.Equal(0, plan.BleedVerticalMm, 3);
        Assert.Equal(100, plan.PlacedPageWidthMm, 3);
        Assert.Equal(140, plan.PlacedPageHeightMm, 3);
    }
}
