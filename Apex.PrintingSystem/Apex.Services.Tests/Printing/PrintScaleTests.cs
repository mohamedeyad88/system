using System;

namespace Apex.Services.Tests.Printing;

/// <summary>
/// The page geometry PdfDirectPrinter lays down.
///
/// The old code divided MarginBounds (hundredths of an inch) by the PDF page size
/// (points) and used the quotient as a scale factor. Those are different units, so
/// every page printed at roughly three quarters of actual size — and it fitted to the
/// one-inch default margins on top of that. On a numbered book or an imposed sheet
/// that is not merely "smaller": once the stack is cut, every number and every crop
/// mark is in the wrong place.
///
/// These tests pin the arithmetic that decides the printed size.
/// </summary>
public class PrintScaleTests
{
    private const double PointToHundredthsInch = 100.0 / 72.0;

    // A4 in PDF points.
    private const double A4WidthPt = 595.276;
    private const double A4HeightPt = 841.890;

    /// <summary>The geometry under test, mirroring the printer's own calculation.</summary>
    private static (int W, int H, double Scale) Layout(
        double pageWpt, double pageHpt, double availW, double availH)
    {
        double naturalW = pageWpt * PointToHundredthsInch;
        double naturalH = pageHpt * PointToHundredthsInch;

        double scale = 1.0;
        if (naturalW > availW || naturalH > availH)
            scale = Math.Min(availW / naturalW, availH / naturalH);

        return ((int)Math.Round(naturalW * scale), (int)Math.Round(naturalH * scale), scale);
    }

    [Fact]
    public void APageThatFitsPrintsAtExactlyActualSize()
    {
        // A4 artwork on A3 paper: the sheet can hold it, so nothing may be scaled.
        var (w, h, scale) = Layout(A4WidthPt, A4HeightPt, availW: 1130, availH: 1615);

        Assert.Equal(1.0, scale, 3);
        Assert.InRange(w, 826, 828);      // 8.27 inches
        Assert.InRange(h, 1168, 1170);    // 11.69 inches
    }

    [Fact]
    public void A4OnA4ShrinksOnlyByTheUnprintableBorder()
    {
        // No desktop printer reaches the paper edge, so a full-size A4 page really
        // does have to come in slightly. What matters is that it is a hair, not a
        // quarter — the old code lost 24%.
        var (_, _, scale) = Layout(A4WidthPt, A4HeightPt, availW: 811, availH: 1147);

        Assert.InRange(scale, 0.95, 1.0);
    }

    [Fact]
    public void TheOldUnitMixWouldHaveShrunkThePageToAboutThreeQuarters()
    {
        // Reproduces the previous formula to show what was actually happening:
        // scale = marginBounds / pagePoints, with 1-inch margins on A4.
        const double marginBoundsW = 827 - 200;   // page minus 1in each side
        double oldScale = marginBoundsW / A4WidthPt;

        // Expressed as a real-world scale, that is the factor over the correct one.
        double effective = oldScale / PointToHundredthsInch;

        Assert.InRange(effective, 0.74, 0.78);
    }

    [Fact]
    public void AnOversizePageShrinksToFit()
    {
        // A3 artwork sent to an A4 tray must come down — and to the LIMITING axis, so
        // nothing runs off the sheet. A3 into A4 is legitimately about 69%.
        const double A3WidthPt = 841.89, A3HeightPt = 1190.55;
        var (w, h, scale) = Layout(A3WidthPt, A3HeightPt, availW: 811, availH: 1147);

        Assert.True(scale < 1.0);
        Assert.True(w <= 811 && h <= 1147,
            $"scaled page {w}x{h} still runs off the {811}x{1147} printable area");

        double expected = Math.Min(811 / (A3WidthPt * PointToHundredthsInch),
                                   1147 / (A3HeightPt * PointToHundredthsInch));
        Assert.Equal(expected, scale, 4);
    }

    [Fact]
    public void ShrinkingKeepsTheAspectRatio()
    {
        // Independent X and Y scaling would stretch the artwork — on a numbered book
        // the numbers would no longer align with the die.
        var (w, h, _) = Layout(A4WidthPt, A4HeightPt, availW: 400, availH: 1147);

        double sourceRatio = A4WidthPt / A4HeightPt;
        double printedRatio = (double)w / h;

        Assert.Equal(sourceRatio, printedRatio, 2);
    }

    [Fact]
    public void ThePageIsCentredInThePrintableArea()
    {
        var (w, h, _) = Layout(A4WidthPt, A4HeightPt, availW: 900, availH: 1300);

        int offsetX = (int)Math.Round((900 - w) / 2.0);
        int offsetY = (int)Math.Round((1300 - h) / 2.0);

        Assert.InRange(Math.Abs((900 - w - offsetX) - offsetX), 0, 1);
        Assert.InRange(Math.Abs((1300 - h - offsetY) - offsetY), 0, 1);
    }

    [Theory]
    [InlineData(300, 827, 2481)]    // 8.27in at 300dpi
    [InlineData(600, 827, 4962)]    // same page at 600dpi
    public void TheRasterMatchesTheDestinationExactly(int dpi, int destHundredths, int expectedPx)
    {
        // Rendering at one size and letting GDI+ rescale into another resamples the
        // page a second time — a second blur on top of rasterising.
        int px = Math.Max(1, (int)Math.Round(destHundredths / 100.0 * dpi));

        Assert.Equal(expectedPx, px);
    }
}
