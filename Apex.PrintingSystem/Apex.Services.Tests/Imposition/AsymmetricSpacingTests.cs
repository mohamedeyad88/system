using Apex.Core.Models.Imposition;
using Apex.Services.Imposition;
using Apex.Services.PaperCutting;

namespace Apex.Services.Tests.Imposition;

/// <summary>
/// Per-edge margins and split horizontal/vertical gutters and bleed.
///
/// The driver is the GRIPPER edge: the press jaws hold one edge of the sheet, so
/// that edge needs more clearance than the other three. Before this, a single
/// margin forced the operator to waste the same amount on all four edges.
/// </summary>
public class AsymmetricSpacingTests
{
    private static ImpositionService NewService() => new(new PaperCuttingOptimizerService());

    private static ImpositionInput Base() => new()
    {
        Type = ImpositionType.NUp,
        NUp = NUpLayout.FourUp,
        SourcePageCount = 4,
        PageWidth = 100,
        PageHeight = 70,
        SheetWidth = 320,
        SheetHeight = 450,
        Bleed = 0,
        SheetMargin = 10,
        Gutter = 5,
        AllowRotation = false,
        Alignment = SheetAlignment.TopLeft,   // pin to the origin so positions are exact
    };

    [Fact]
    public void WithoutOverrides_UniformValuesStillApply()
    {
        var r = NewService().Plan(Base());

        Assert.True(r.IsValid, r.ErrorMessage);
        var first = r.Sheets[0].Front.Slots.OrderBy(s => s.Y).ThenBy(s => s.X).First();
        Assert.Equal(10, first.X, 6);
        Assert.Equal(10, first.Y, 6);
    }

    [Fact]
    public void GripperEdgeMargin_MovesTheGridAwayFromThatEdgeOnly()
    {
        var input = Base();
        input.MarginLeft = 30;      // gripper edge
        input.MarginTop = 8;

        var r = NewService().Plan(input);

        Assert.True(r.IsValid, r.ErrorMessage);
        var first = r.Sheets[0].Front.Slots.OrderBy(s => s.Y).ThenBy(s => s.X).First();
        Assert.Equal(30, first.X, 6);
        Assert.Equal(8, first.Y, 6);
    }

    [Fact]
    public void HorizontalAndVerticalGutters_AreIndependent()
    {
        var input = Base();
        // 100×140 on a 320×450 sheet leaves 2×2 as the only grid that fits, so the
        // assertions below always have both a column gap and a row gap to measure.
        input.PageWidth = 100;
        input.PageHeight = 140;
        input.GutterHorizontal = 20;
        input.GutterVertical = 2;

        var r = NewService().Plan(input);
        Assert.True(r.IsValid, r.ErrorMessage);

        var slots = r.Sheets[0].Front.Slots;
        var topRow = slots.Where(s => Math.Abs(s.Y - slots.Min(x => x.Y)) < 1e-6)
                          .OrderBy(s => s.X).ToList();
        Assert.True(topRow.Count >= 2, "test needs at least 2 columns");

        // Column step = page width + horizontal gutter.
        double columnGap = topRow[1].X - (topRow[0].X + topRow[0].Width);
        Assert.Equal(20, columnGap, 6);

        var leftCol = slots.Where(s => Math.Abs(s.X - slots.Min(x => x.X)) < 1e-6)
                           .OrderBy(s => s.Y).ToList();
        if (leftCol.Count >= 2)
        {
            double rowGap = leftCol[1].Y - (leftCol[0].Y + leftCol[0].Height);
            Assert.Equal(2, rowGap, 6);
        }
    }

    [Fact]
    public void HorizontalAndVerticalBleed_AreIndependent()
    {
        var input = Base();
        input.BleedHorizontal = 5;
        input.BleedVertical = 1;

        var r = NewService().Plan(input);
        Assert.True(r.IsValid, r.ErrorMessage);

        var slot = r.Sheets[0].Front.Slots[0];
        Assert.Equal(100 + 5 * 2, slot.Width, 6);
        Assert.Equal(70 + 1 * 2, slot.Height, 6);
    }

    [Fact]
    public void PartialOverride_FallsBackToTheUniformValueForTheOtherEdges()
    {
        var input = Base();
        input.MarginLeft = 25;      // only this edge overridden

        var r = NewService().Plan(input);

        Assert.True(r.IsValid, r.ErrorMessage);
        var first = r.Sheets[0].Front.Slots.OrderBy(s => s.Y).ThenBy(s => s.X).First();
        Assert.Equal(25, first.X, 6);
        Assert.Equal(10, first.Y, 6);   // still the uniform SheetMargin
    }

    [Fact]
    public void CentredAlignment_RespectsAsymmetricMargins()
    {
        var input = Base();
        input.Alignment = SheetAlignment.Centre;
        input.MarginLeft = 40;
        input.MarginRight = 10;

        var r = NewService().Plan(input);
        Assert.True(r.IsValid, r.ErrorMessage);

        var slots = r.Sheets[0].Front.Slots;
        double left = slots.Min(s => s.X);
        double right = slots.Max(s => s.X + s.Width);

        // The grid must stay inside the asymmetric printable band.
        Assert.True(left >= 40 - 1e-6, $"grid crossed the left margin ({left})");
        Assert.True(right <= r.SheetWidthMm - 10 + 1e-6, $"grid crossed the right margin ({right})");
    }

    [Fact]
    public void OversizedGripperMargin_FailsCleanlyInsteadOfOverflowing()
    {
        var input = Base();
        input.MarginLeft = 200;
        input.MarginRight = 200;    // leaves no printable width

        var r = NewService().Plan(input);

        Assert.False(r.IsValid);
        Assert.False(string.IsNullOrWhiteSpace(r.ErrorMessage));
    }

    [Fact]
    public void AllSlotsStayInsideTheDeclaredMargins()
    {
        var input = Base();
        input.MarginLeft = 20;
        input.MarginTop = 15;
        input.MarginRight = 8;
        input.MarginBottom = 12;

        var r = NewService().Plan(input);
        Assert.True(r.IsValid, r.ErrorMessage);

        foreach (var s in r.Sheets.SelectMany(sh => sh.Front.Slots))
        {
            Assert.True(s.X >= 20 - 1e-6, "slot crossed the left margin");
            Assert.True(s.Y >= 15 - 1e-6, "slot crossed the top margin");
            Assert.True(s.X + s.Width <= r.SheetWidthMm - 8 + 1e-6, "slot crossed the right margin");
            Assert.True(s.Y + s.Height <= r.SheetHeightMm - 12 + 1e-6, "slot crossed the bottom margin");
        }
    }

    [Fact]
    public void AsymmetricSpacing_AlsoAppliesToBookletSchemes()
    {
        var input = Base();
        input.Type = ImpositionType.SaddleStitch;
        input.SourcePageCount = 8;
        input.PageWidth = 100;
        input.PageHeight = 140;
        input.MarginTop = 30;

        var r = NewService().Plan(input);

        Assert.True(r.IsValid, r.ErrorMessage);
        foreach (var s in r.Sheets.SelectMany(sh => sh.Front.Slots))
            Assert.True(s.Y >= 30 - 1e-6, "booklet slot crossed the top margin");
    }
}
