using Apex.UI.Numbering;
using Xunit;

namespace Apex.UI.Tests;

public class SlotAutoFitTests
{
    // An A4 design at 300 dpi, the usual case.
    private const double CanvasW = 2480;
    private const double CanvasH = 3508;

    [Fact]
    public void ABoxIsTheSizeOfItsNumber_NotAThirdOfTheSheet()
    {
        // "000001" at the default 24pt on a 300 dpi A4: roughly 230 x 80 canvas px.
        var (width, height) = SlotAutoFit.Normalized(230, 80, CanvasW, CanvasH);

        Assert.InRange(width, 0.09, 0.12);   // was a flat 0.30
        Assert.InRange(height, 0.02, 0.04);  // was a flat 0.08
    }

    [Fact]
    public void AWiderNumberGetsAWiderBox()
    {
        var plain = SlotAutoFit.Normalized(230, 80, CanvasW, CanvasH);
        var withPrefix = SlotAutoFit.Normalized(460, 80, CanvasW, CanvasH);

        Assert.True(withPrefix.Width > plain.Width * 1.8);
        Assert.Equal(plain.Height, withPrefix.Height, 4);
    }

    [Theory]
    [InlineData("Center", 0.30f, 0.10f, 0.20f)] // centre stays: x moves in by half the difference
    [InlineData("Left", 0.30f, 0.10f, 0.10f)]   // left edge stays
    [InlineData("Right", 0.30f, 0.10f, 0.30f)]  // right edge stays
    public void ShrinkingTheBoxDoesNotMoveTheNumber(string alignment, float oldWidth, float newWidth, float expectedX)
    {
        var (x, _) = SlotAutoFit.KeepAnchor(alignment, x: 0.10f, y: 0.20f,
            oldWidth: oldWidth, oldHeight: 0.08f, newWidth: newWidth, newHeight: 0.03f);

        Assert.Equal(expectedX, x, 3);
    }

    [Fact]
    public void VerticallyTheNumberStaysWhereItWas()
    {
        var (_, y) = SlotAutoFit.KeepAnchor("Center", 0.1f, 0.20f, 0.30f, 0.08f, 0.10f, 0.03f);

        // The old box spanned 0.20–0.28, centre 0.24. The new 0.03-tall box keeps that centre.
        Assert.Equal(0.225f, y, 3);
    }

    [Fact]
    public void ABoxNeverLeavesTheSheet()
    {
        var (x, y) = SlotAutoFit.KeepAnchor("Right", x: 0.97f, y: 0.99f,
            oldWidth: 0.02f, oldHeight: 0.01f, newWidth: 0.30f, newHeight: 0.20f);

        Assert.InRange(x, 0f, 1f);
        Assert.InRange(y, 0f, 1f);
    }

    [Fact]
    public void NothingToMeasure_LeavesTheBoxAlone() =>
        Assert.Equal((0f, 0f), SlotAutoFit.Normalized(0, 0, CanvasW, CanvasH));

    [Fact]
    public void TheCanvasScaleMatchesWhatTheDesignerDraws()
    {
        Assert.Equal(1.0, SlotAutoFit.CanvasFontScale(794), 2);    // A4 at 96 dpi → no scaling
        Assert.Equal(3.12, SlotAutoFit.CanvasFontScale(2480), 2);  // A4 at 300 dpi
        Assert.Equal(5.0, SlotAutoFit.CanvasFontScale(99999), 2);  // clamped
    }
}
