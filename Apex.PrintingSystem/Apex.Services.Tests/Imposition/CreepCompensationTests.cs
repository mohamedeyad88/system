using Apex.Core.Models.Imposition;
using Apex.Services.Imposition;
using Apex.Services.PaperCutting;

namespace Apex.Services.Tests.Imposition;

/// <summary>
/// Creep (shingling) compensation for folded signatures.
///
/// Nested sheets push the inner pages toward the fore-edge, so after trimming the
/// inner pages lose more margin than the outer ones. Compensation pre-shifts each
/// nesting level toward the spine. Getting the DIRECTION wrong makes the problem
/// twice as bad, so the direction is asserted explicitly.
/// </summary>
public class CreepCompensationTests
{
    private static ImpositionService NewService() => new(new PaperCuttingOptimizerService());

    private static ImpositionInput Booklet(
        ImpositionType type = ImpositionType.SaddleStitch,
        int pages = 32,
        double thickness = 0.1) => new()
    {
        Type = type,
        SourcePageCount = pages,
        PageWidth = 140,
        PageHeight = 200,
        SheetWidth = 320,
        SheetHeight = 450,
        Bleed = 3,
        SheetMargin = 10,
        Gutter = 5,
        PaperThickness = thickness,
        LeavesPerSignature = 4,
    };

    /// <summary>Left page = column 0, right page = column 1 of the 2-up saddle grid.</summary>
    private static (PageSlot left, PageSlot right) Pages(SheetSide side)
    {
        var ordered = side.Slots.OrderBy(s => s.X).ToList();
        return (ordered[0], ordered[1]);
    }

    [Fact]
    public void WithoutThickness_NoCompensationIsApplied()
    {
        var r = NewService().Plan(Booklet(thickness: 0));

        Assert.True(r.IsValid, r.ErrorMessage);
        Assert.Equal(0, r.MaxCreepMm);

        // Every sheet sits at identical positions.
        var first = Pages(r.Sheets[0].Front);
        foreach (var sheet in r.Sheets)
        {
            var p = Pages(sheet.Front);
            Assert.Equal(first.left.X, p.left.X, 6);
            Assert.Equal(first.right.X, p.right.X, 6);
        }
    }

    [Fact]
    public void OutermostSheetIsNeverShifted()
    {
        var withCreep = NewService().Plan(Booklet());
        var without = NewService().Plan(Booklet(thickness: 0));

        var a = Pages(withCreep.Sheets[0].Front);
        var b = Pages(without.Sheets[0].Front);

        Assert.Equal(b.left.X, a.left.X, 6);
        Assert.Equal(b.right.X, a.right.X, 6);
    }

    [Fact]
    public void InnerPagesShiftTowardTheSpine_NotAwayFromIt()
    {
        var r = NewService().Plan(Booklet());
        Assert.True(r.Sheets.Count > 1);

        var outer = Pages(r.Sheets[0].Front);
        var inner = Pages(r.Sheets[^1].Front);

        // The spine is between the two pages: the left page moves RIGHT and the
        // right page moves LEFT as nesting deepens.
        Assert.True(inner.left.X > outer.left.X,
            $"left page must move toward the spine ({outer.left.X} → {inner.left.X})");
        Assert.True(inner.right.X < outer.right.X,
            $"right page must move toward the spine ({outer.right.X} → {inner.right.X})");
    }

    [Fact]
    public void ShiftGrowsMonotonicallyWithNestingDepth()
    {
        var r = NewService().Plan(Booklet());

        double previous = double.NegativeInfinity;
        foreach (var sheet in r.Sheets)
        {
            double shift = Pages(sheet.Front).left.X;
            Assert.True(shift >= previous - 1e-9, "creep must not decrease going inward");
            previous = shift;
        }
    }

    [Fact]
    public void BothSidesOfASheetGetTheSameCompensation()
    {
        var r = NewService().Plan(Booklet());
        var sheet = r.Sheets[^1];

        var front = Pages(sheet.Front);
        var back = Pages(sheet.Back!);

        Assert.Equal(front.left.X, back.left.X, 6);
        Assert.Equal(front.right.X, back.right.X, 6);
    }

    [Fact]
    public void MaxCreepIsReportedAndWarned()
    {
        var r = NewService().Plan(Booklet());

        Assert.True(r.MaxCreepMm > 0);
        Assert.NotEmpty(r.Warnings);
    }

    [Fact]
    public void CompensationNeverPushesPagesAcrossTheSpine()
    {
        // An absurd caliper must be clamped, not allowed to overlap the pages.
        var r = NewService().Plan(Booklet(thickness: 50));

        Assert.True(r.IsValid, r.ErrorMessage);
        foreach (var sheet in r.Sheets)
        {
            var (left, right) = Pages(sheet.Front);
            Assert.True(left.X + left.Width <= right.X + 1e-6,
                "compensated pages must not overlap across the spine");
        }
    }

    [Fact]
    public void PerfectBinding_ResetsCreepPerSignature()
    {
        var r = NewService().Plan(Booklet(ImpositionType.PerfectBinding, pages: 64));

        Assert.True(r.IsValid, r.ErrorMessage);
        Assert.True(r.SignatureCount > 1, "test needs multiple signatures");

        // The first sheet of every signature is that signature's outermost sheet,
        // so its shift must match — creep does not accumulate across signatures.
        var firstOfSignature = r.Sheets
            .GroupBy(s => s.SignatureIndex)
            .Select(g => Pages(g.First().Front).left.X)
            .Distinct()
            .ToList();

        Assert.Single(firstOfSignature);
    }

    [Fact]
    public void SingleSheetSignature_HasNoCreep()
    {
        // 4 pages = one folded sheet: nothing is nested inside anything.
        var r = NewService().Plan(Booklet(pages: 4));

        Assert.Equal(0, r.MaxCreepMm);
    }

    [Fact]
    public void CompensationDoesNotChangePageOrder()
    {
        var withCreep = NewService().Plan(Booklet());
        var without = NewService().Plan(Booklet(thickness: 0));

        for (int i = 0; i < withCreep.Sheets.Count; i++)
        {
            Assert.Equal(
                without.Sheets[i].Front.Slots.Select(s => s.SourcePageNumber),
                withCreep.Sheets[i].Front.Slots.Select(s => s.SourcePageNumber));
        }
    }
}
