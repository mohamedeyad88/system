using Apex.Services.SmartVariables;
using Apex.Services.SmartVariables.Models;

namespace Apex.Services.Tests.SmartVariables;

/// <summary>
/// Making text fit the box it was given.
///
/// The field model has offered ShrinkToFit and MinFontSize from the start and nothing
/// implemented them — and ShrinkToFit is the DEFAULT. On a run of several thousand
/// certificates, every unusually long name silently spilled its box or was clipped
/// mid-word, and nobody saw it until the job was on paper.
/// </summary>
public class CopyFitTests
{
    /// <summary>
    /// A predictable stand-in for real text metrics: width grows linearly with size,
    /// height is one line. Enough to pin the fitting RULES without a rendering stack.
    /// </summary>
    private static MeasureText Measure(double widthPerCharPerPoint = 0.5) =>
        (text, size) => (text.Length * widthPerCharPerPoint * size, size * 1.2);

    [Fact]
    public void TextThatAlreadyFitsIsNotShrunk()
    {
        // Shrinking when there is no need makes a set of cards typographically uneven.
        var r = CopyFitCalculator.Fit("Ali", boxWidth: 100, boxHeight: 20,
            startSize: 12, minSize: 6, Measure());

        Assert.True(r.Fits);
        Assert.Equal(12, r.FontSize);
    }

    [Fact]
    public void LongTextIsShrunkUntilItFits()
    {
        // 38 characters in a 200-wide box: at 24pt it needs 456, so it must shrink —
        // but it is still comfortably above the 6pt floor.
        const string longName = "Mohamed Abd El-Rahman El-Sayed Ibrahim";

        var r = CopyFitCalculator.Fit(longName, boxWidth: 200, boxHeight: 20,
            startSize: 24, minSize: 6, Measure());

        Assert.True(r.Fits);
        Assert.True(r.FontSize < 24, "the text was not shrunk at all");
        Assert.True(r.OverflowRatio <= 1.0,
            $"still overflows by {r.OverflowRatio:P0} at the chosen size");
    }

    [Fact]
    public void TheChosenSizeIsTheLargestThatFits()
    {
        // A needlessly small size wastes the box and looks wrong next to its neighbours.
        var measure = Measure();
        var r = CopyFitCalculator.Fit("Abdelrahman", 100, 40, 40, 6, measure);

        Assert.True(r.Fits);

        var (w, h) = measure("Abdelrahman", r.FontSize + 1.0);
        Assert.True(w > 100 || h > 40,
            $"size {r.FontSize} was chosen but {r.FontSize + 1} would also have fitted");
    }

    [Fact]
    public void TextThatCannotFitEvenAtTheFloorIsReportedAsNotFitting()
    {
        // The important case. Returning a smaller unreadable size would hide the
        // problem; the operator has to be able to catch that record.
        var r = CopyFitCalculator.Fit(new string('X', 500), boxWidth: 20, boxHeight: 10,
            startSize: 12, minSize: 6, Measure());

        Assert.False(r.Fits);
        Assert.Equal(6, r.FontSize);
    }

    [Fact]
    public void NeverGoesBelowTheLegibilityFloor()
    {
        var r = CopyFitCalculator.Fit(new string('X', 200), 50, 20, 24, 8, Measure());

        Assert.True(r.FontSize >= 8, $"chose {r.FontSize}pt, below the 8pt floor");
    }

    [Fact]
    public void AnEmptyFieldIsNotAnOverflow()
    {
        var r = CopyFitCalculator.Fit("", 10, 10, 12, 6, Measure());

        Assert.True(r.Fits);
        Assert.Equal(12, r.FontSize);
    }

    [Fact]
    public void HeightConstrainsAsWellAsWidth()
    {
        // A short word in a shallow box still has to shrink.
        var r = CopyFitCalculator.Fit("Hi", boxWidth: 1000, boxHeight: 10,
            startSize: 40, minSize: 4, Measure());

        Assert.True(r.Fits);
        Assert.True(r.FontSize <= 10 / 1.2 + 0.25, $"{r.FontSize}pt is taller than the box");
    }

    [Fact]
    public void FittingAFieldAllowsForItsPadding()
    {
        // Text lives inside the padding; ignoring it puts the last character under the
        // border on every piece.
        var field = new SmartTemplateField
        {
            Width = 100, Height = 30,
            PaddingLeft = 10, PaddingRight = 10, PaddingTop = 5, PaddingBottom = 5,
            FontSize = 20, MinFontSize = 4,
        };

        var measure = Measure();
        var r = CopyFitCalculator.Fit(field, "Mohamed Ibrahim", measure);

        Assert.True(r.Fits);
        var (w, h) = measure("Mohamed Ibrahim", r.FontSize);
        Assert.True(w <= 80.001, $"width {w} exceeds the padded box of 80");
        Assert.True(h <= 20.001, $"height {h} exceeds the padded box of 20");
    }

    [Fact]
    public void ABoxWithNoRoomIsReportedRatherThanDividedByZero()
    {
        var r = CopyFitCalculator.Fit("text", boxWidth: 0, boxHeight: 0, 12, 6, Measure());

        Assert.False(r.Fits);
    }
}
