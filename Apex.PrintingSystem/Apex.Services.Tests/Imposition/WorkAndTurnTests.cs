using Apex.Core.Models.Imposition;
using Apex.Services.Imposition;
using Apex.Services.PaperCutting;

namespace Apex.Services.Tests.Imposition;

/// <summary>
/// Press semantics for the single-plate schemes.
///
/// The defining property: ONE plate carries both sides of the piece, the sheet is
/// flipped and printed again from that same plate, so every sheet yields TWO
/// finished copies. If the back positions are wrong the job backs up misregistered
/// and the whole run is scrap — hence the explicit geometry assertions.
/// </summary>
public class WorkAndTurnTests
{
    private static ImpositionService NewService() => new(new PaperCuttingOptimizerService());

    private static ImpositionInput Input(ImpositionType type, int pages = 2, int copies = 100) => new()
    {
        Type = type,
        SourcePageCount = pages,
        RequiredCopies = copies,
        PageWidth = 90,
        PageHeight = 50,
        SheetWidth = 320,
        SheetHeight = 450,
        Bleed = 3,
        SheetMargin = 10,
        Gutter = 5,
        AllowRotation = true,
    };

    [Theory]
    [InlineData(ImpositionType.WorkAndTurn)]
    [InlineData(ImpositionType.WorkAndTumble)]
    public void PlansSuccessfully_AndIsDuplex(ImpositionType type)
    {
        var r = NewService().Plan(Input(type));

        Assert.True(r.IsValid, r.ErrorMessage);
        Assert.True(r.IsDuplex);
        Assert.NotEmpty(r.Sheets);
    }

    [Theory]
    [InlineData(100, 50)]   // each sheet yields 2 copies
    [InlineData(101, 51)]   // odd count rounds up
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    public void EachSheetYieldsTwoCopies(int copies, int expectedSheets)
    {
        var r = NewService().Plan(Input(ImpositionType.WorkAndTurn, copies: copies));

        Assert.True(r.IsValid, r.ErrorMessage);
        Assert.Equal(expectedSheets, r.SheetsRequired);
    }

    [Theory]
    [InlineData(ImpositionType.WorkAndTurn)]
    [InlineData(ImpositionType.WorkAndTumble)]
    public void BothSidesCarryTheSamePages_BecauseItIsOnePlate(ImpositionType type)
    {
        var r = NewService().Plan(Input(type));
        var sheet = r.Sheets[0];

        Assert.NotNull(sheet.Back);
        Assert.Equal(sheet.Front.Slots.Count, sheet.Back!.Slots.Count);

        var front = sheet.Front.Slots.Select(s => s.SourcePageNumber).ToList();
        var back = sheet.Back.Slots.Select(s => s.SourcePageNumber).ToList();
        Assert.Equal(front, back);
    }

    [Fact]
    public void WorkAndTurn_FlipsAboutTheVerticalAxis_KeepingTheGripperEdge()
    {
        var r = NewService().Plan(Input(ImpositionType.WorkAndTurn));
        var sheet = r.Sheets[0];

        for (int i = 0; i < sheet.Front.Slots.Count; i++)
        {
            var f = sheet.Front.Slots[i];
            var b = sheet.Back!.Slots[i];

            // Mirrored left↔right …
            Assert.Equal(r.SheetWidthMm - f.X - f.Width, b.X, 6);
            // … and unchanged top-to-bottom: the gripper edge stays put.
            Assert.Equal(f.Y, b.Y, 6);
        }
    }

    [Fact]
    public void WorkAndTumble_FlipsAboutTheHorizontalAxis_MovingTheGripperEdge()
    {
        var r = NewService().Plan(Input(ImpositionType.WorkAndTumble));
        var sheet = r.Sheets[0];

        for (int i = 0; i < sheet.Front.Slots.Count; i++)
        {
            var f = sheet.Front.Slots[i];
            var b = sheet.Back!.Slots[i];

            Assert.Equal(f.X, b.X, 6);
            Assert.Equal(r.SheetHeightMm - f.Y - f.Height, b.Y, 6);
        }
    }

    [Fact]
    public void OddPageCount_IsPaddedToAnEvenFrontAndBack()
    {
        var r = NewService().Plan(Input(ImpositionType.WorkAndTurn, pages: 3));

        Assert.True(r.IsValid, r.ErrorMessage);
        Assert.Equal(1, r.PaddingPages);
        // The padded slot is a deliberate blank, not page 0 content.
        Assert.Contains(r.Sheets[0].Front.Slots, s => s.IsBlank);
    }

    [Fact]
    public void EvenPageCount_NeedsNoPadding()
    {
        var r = NewService().Plan(Input(ImpositionType.WorkAndTurn, pages: 4));

        Assert.Equal(0, r.PaddingPages);
        Assert.DoesNotContain(r.Sheets[0].Front.Slots, s => s.IsBlank);
    }

    [Theory]
    [InlineData(ImpositionType.WorkAndTurn)]
    [InlineData(ImpositionType.WorkAndTumble)]
    public void OperatorGetsFlipInstructions(ImpositionType type)
    {
        var r = NewService().Plan(Input(type));

        // The scheme is useless — and dangerous — without telling the operator
        // which way to flip the sheet.
        Assert.NotEmpty(r.Warnings);
    }

    [Fact]
    public void MirrorBacksOption_DoesNotDoubleMirrorTheFlipPositions()
    {
        var plain = NewService().Plan(Input(ImpositionType.WorkAndTurn));

        var mirroredInput = Input(ImpositionType.WorkAndTurn);
        mirroredInput.MirrorBacksHorizontally = true;
        var mirrored = NewService().Plan(mirroredInput);

        // The back is already defined by the physical flip; the generic mirror
        // post-step must not apply on top of it.
        for (int i = 0; i < plain.Sheets[0].Back!.Slots.Count; i++)
        {
            Assert.Equal(plain.Sheets[0].Back!.Slots[i].X,
                         mirrored.Sheets[0].Back!.Slots[i].X, 6);
        }
    }

    [Fact]
    public void RejectsNonPositivePageCount()
    {
        var r = NewService().Plan(Input(ImpositionType.WorkAndTurn, pages: 0));
        Assert.False(r.IsValid);
    }

    [Fact]
    public void RejectsNonPositiveCopies()
    {
        var r = NewService().Plan(Input(ImpositionType.WorkAndTurn, copies: 0));
        Assert.False(r.IsValid);
    }

    [Fact]
    public void PageLargerThanSheet_FailsWithAMessageInsteadOfThrowing()
    {
        var input = Input(ImpositionType.WorkAndTurn);
        input.PageWidth = 900;
        input.PageHeight = 900;

        var r = NewService().Plan(input);

        Assert.False(r.IsValid);
        Assert.False(string.IsNullOrWhiteSpace(r.ErrorMessage));
    }

    [Fact]
    public void SlotsStayInsideTheSheet()
    {
        var r = NewService().Plan(Input(ImpositionType.WorkAndTurn, pages: 4));
        var sheet = r.Sheets[0];

        foreach (var s in sheet.Front.Slots.Concat(sheet.Back!.Slots))
        {
            Assert.True(s.X >= -1e-6, $"slot X {s.X} outside sheet");
            Assert.True(s.Y >= -1e-6, $"slot Y {s.Y} outside sheet");
            Assert.True(s.X + s.Width <= r.SheetWidthMm + 1e-6, "slot overflows sheet width");
            Assert.True(s.Y + s.Height <= r.SheetHeightMm + 1e-6, "slot overflows sheet height");
        }
    }
}
