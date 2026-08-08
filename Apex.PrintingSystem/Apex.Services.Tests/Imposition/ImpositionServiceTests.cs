using Apex.Core.Models.Imposition;
using Apex.Services.Imposition;
using Apex.Services.PaperCutting;

namespace Apex.Services.Tests.Imposition;

/// <summary>
/// Unit tests for <see cref="ImpositionService"/> (Phase 1 — layout/ordering logic).
/// All dimensions are millimetres.
/// </summary>
public class ImpositionServiceTests
{
    private readonly ImpositionService _svc = new(new PaperCuttingOptimizerService());

    private static ImpositionInput Base() => new()
    {
        PageWidth = 210,
        PageHeight = 297,
        SheetWidth = 320,
        SheetHeight = 450,
        SourcePageCount = 8,
        Bleed = 0,
        SheetMargin = 0,
        Gutter = 0,
        AllowRotation = true,
        Unit = Apex.Core.Models.PaperCutting.MeasurementUnit.Millimeter
    };

    /// <summary>
    /// Booklet schemes place two pages side by side across the spine, so two A4
    /// pages need 420 mm of sheet width — the sheet must be fed LANDSCAPE
    /// (450×320), not portrait. Using the portrait sheet asks for 420 mm of content
    /// on a 320 mm sheet, which the planner now correctly rejects instead of
    /// silently laying the pages off the edge.
    /// </summary>
    private static ImpositionInput BookletBase()
    {
        var i = Base();
        i.SheetWidth = 450;
        i.SheetHeight = 320;
        return i;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TC-01  N-Up 2-Up: A4 pages on SRA3 → 1 row × 2 cols, sequential order
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void TC01_NUp_TwoUp_BuildsGridAndSequentialPages()
    {
        var input = Base();
        input.Type = ImpositionType.NUp;
        input.NUp = NUpLayout.TwoUp;
        input.SourcePageCount = 4;

        var r = _svc.Plan(input);

        Assert.True(r.IsValid, r.ErrorMessage);
        Assert.Equal(2, r.PagesPerSide);
        Assert.Equal(2, r.SheetsRequired);             // 4 pages / 2 per sheet
        Assert.Equal(2, r.Sheets[0].Front.Slots.Count);
        Assert.Equal(1, r.Sheets[0].Front.Slots[0].SourcePageNumber);
        Assert.Equal(2, r.Sheets[0].Front.Slots[1].SourcePageNumber);
        Assert.False(r.IsDuplex);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TC-02  N-Up 4-Up grid is 2×2 and last sheet fills partial pages
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void TC02_NUp_FourUp_PartialLastSheet()
    {
        var input = Base();
        input.Type = ImpositionType.NUp;
        input.NUp = NUpLayout.FourUp;
        input.PageWidth = 100; input.PageHeight = 150;
        input.SheetWidth = 320; input.SheetHeight = 450;
        input.SourcePageCount = 5;

        var r = _svc.Plan(input);

        Assert.True(r.IsValid, r.ErrorMessage);
        Assert.Equal(4, r.PagesPerSide);
        Assert.Equal(2, r.SheetsRequired);             // ceil(5/4)
        Assert.Equal(4, r.Sheets[0].Front.Slots.Count);
        Assert.Single(r.Sheets[1].Front.Slots);        // remaining 1 page
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TC-03  Saddle Stitch ordering for 8 pages (2 sheets, duplex)
    //   Sheet 0 front [8,1] back [2,7]; Sheet 1 front [6,3] back [4,5]
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void TC03_SaddleStitch_BookletOrder()
    {
        var input = BookletBase();
        input.Type = ImpositionType.SaddleStitch;
        input.SourcePageCount = 8;

        var r = _svc.Plan(input);

        Assert.True(r.IsValid, r.ErrorMessage);
        Assert.True(r.IsDuplex);
        Assert.Equal(2, r.SheetsRequired);
        Assert.Equal(0, r.PaddingPages);

        // Sheet 0
        Assert.Equal(8, r.Sheets[0].Front.Slots[0].SourcePageNumber);
        Assert.Equal(1, r.Sheets[0].Front.Slots[1].SourcePageNumber);
        Assert.Equal(2, r.Sheets[0].Back!.Slots[0].SourcePageNumber);
        Assert.Equal(7, r.Sheets[0].Back!.Slots[1].SourcePageNumber);
        // Sheet 1
        Assert.Equal(6, r.Sheets[1].Front.Slots[0].SourcePageNumber);
        Assert.Equal(3, r.Sheets[1].Front.Slots[1].SourcePageNumber);
        Assert.Equal(4, r.Sheets[1].Back!.Slots[0].SourcePageNumber);
        Assert.Equal(5, r.Sheets[1].Back!.Slots[1].SourcePageNumber);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TC-04  Saddle Stitch pads page count to a multiple of 4
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void TC04_SaddleStitch_PadsToMultipleOfFour()
    {
        var input = BookletBase();
        input.Type = ImpositionType.SaddleStitch;
        input.SourcePageCount = 6;

        var r = _svc.Plan(input);

        Assert.True(r.IsValid);
        Assert.Equal(2, r.PaddingPages);               // 6 → 8
        Assert.Equal(2, r.SheetsRequired);
        Assert.NotEmpty(r.Warnings);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TC-05  Perfect Binding splits into signatures (8-page sigs from 20 pages)
    //   leaves=2 → 8 pages/sig → 3 signatures (24 padded), 2 sheets/sig → 6 sheets
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void TC05_PerfectBinding_SplitsIntoSignatures()
    {
        var input = BookletBase();
        input.Type = ImpositionType.PerfectBinding;
        input.LeavesPerSignature = 2;                  // 8 pages per signature
        input.SourcePageCount = 20;

        var r = _svc.Plan(input);

        Assert.True(r.IsValid, r.ErrorMessage);
        Assert.Equal(3, r.SignatureCount);             // ceil(20/8) = 3
        Assert.Equal(4, r.PaddingPages);               // 24 - 20
        Assert.Equal(6, r.SheetsRequired);             // 3 sigs × 2 sheets
        Assert.All(r.Sheets, s => Assert.InRange(s.SignatureIndex, 1, 3));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TC-06  Step & Repeat fills the sheet (reuses paper-cutting optimizer)
    //   90×50 card on 320×450, 100 copies → multiple per sheet, few sheets
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void TC06_StepRepeat_FillsSheet()
    {
        var input = Base();
        input.Type = ImpositionType.StepRepeat;
        input.PageWidth = 90; input.PageHeight = 50;
        input.SheetWidth = 320; input.SheetHeight = 450;
        input.RequiredCopies = 100;

        var r = _svc.Plan(input);

        Assert.True(r.IsValid, r.ErrorMessage);
        Assert.True(r.PagesPerSide > 1);
        Assert.True(r.SheetsRequired >= 1);
        Assert.All(r.Sheets[0].Front.Slots, s => Assert.Equal(1, s.SourcePageNumber));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TC-07  Cut & Stack cycles page numbers across slots
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void TC07_CutStack_CyclesPageNumbers()
    {
        var input = Base();
        input.Type = ImpositionType.CutStack;
        input.PageWidth = 90; input.PageHeight = 50;
        input.SheetWidth = 320; input.SheetHeight = 450;
        input.SourcePageCount = 3;
        input.RequiredCopies = 50;

        var r = _svc.Plan(input);

        Assert.True(r.IsValid, r.ErrorMessage);
        Assert.True(r.Sheets.Count > 0);
        // First three slots cycle 1,2,3
        var slots = r.Sheets[0].Front.Slots;
        Assert.Equal(1, slots[0].SourcePageNumber);
        Assert.Equal(2, slots[1].SourcePageNumber);
        Assert.Equal(3, slots[2].SourcePageNumber);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TC-07b  Rotated grid must propagate Rotation=90 to slots (distortion fix)
    //   A5+3mm bleed (154×216) on SRA3 320×450 with margin 10 → only the rotated
    //   1×2 grid fits, so slots must carry Rotation=90 and a rotated footprint.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void TC07b_NUp_RotatedGrid_SetsSlotRotation()
    {
        var input = Base();
        input.Type = ImpositionType.NUp;
        input.NUp = NUpLayout.TwoUp;
        input.PageWidth = 148; input.PageHeight = 210;
        input.SheetWidth = 320; input.SheetHeight = 450;
        input.Bleed = 3; input.SheetMargin = 10; input.Gutter = 5;
        input.AllowRotation = true;
        input.SourcePageCount = 2;

        var r = _svc.Plan(input);

        Assert.True(r.IsValid, r.ErrorMessage);
        var slot = r.Sheets[0].Front.Slots[0];
        Assert.Equal(90, slot.Rotation);                    // rotation flag propagated
        Assert.True(slot.Width > slot.Height,               // rotated footprint (216 wide × 154 tall)
            $"Expected rotated footprint, got {slot.Width}x{slot.Height}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TC-08  Validation: page larger than sheet → invalid
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void TC08_Validation_PageLargerThanSheet()
    {
        var input = Base();
        input.Type = ImpositionType.NUp;
        input.PageWidth = 500; input.PageHeight = 700;
        input.SheetWidth = 320; input.SheetHeight = 450;

        var r = _svc.Plan(input);

        Assert.False(r.IsValid);
        Assert.NotEmpty(r.ErrorMessage);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TC-09  Validation: zero page count rejected
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void TC09_Validation_ZeroPages()
    {
        var input = Base();
        input.Type = ImpositionType.NUp;
        input.SourcePageCount = 0;

        var r = _svc.Plan(input);

        Assert.False(r.IsValid);
        Assert.NotEmpty(r.ErrorMessage);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TC-10  Summary is populated for a valid plan
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void TC10_Summary_Populated()
    {
        var input = BookletBase();
        input.Type = ImpositionType.SaddleStitch;
        input.SourcePageCount = 8;

        var r = _svc.Plan(input);

        Assert.True(r.IsValid);
        Assert.NotEmpty(r.HumanReadableSummary);
        // Language-neutral: the box-drawing frame is always present (not localized),
        // so this proves a formatted summary was built regardless of active language.
        Assert.Contains("═", r.HumanReadableSummary);
    }
}
