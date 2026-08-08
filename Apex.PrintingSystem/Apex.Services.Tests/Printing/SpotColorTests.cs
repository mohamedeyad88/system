using System.Collections.Generic;
using Apex.Services.Printing.Color;

namespace Apex.Services.Tests.Printing;

/// <summary>
/// Spot inks and overprint — the ink decisions that get a job reprinted.
///
/// Each rule here corresponds to a real failure: a plate the shop did not know it was
/// quoting for, a white halo around every line of small text after the press drifted a
/// hair, a die line printed as a visible stroke on the finished piece.
/// </summary>
public class SpotColorTests
{
    private static readonly SpotColor Brand =
        new("PANTONE 185 C", 0.00, 0.91, 0.76, 0.00);

    private static readonly SpotColor DieLine =
        new("CutContour", 0.00, 1.00, 0.00, 0.00, IsTechnical: true);

    // ── Plate counting ───────────────────────────────────────────────────────

    [Fact]
    public void SpotInksAddPlatesOnTopOfProcess()
    {
        // Quoting four-colour and discovering two extra plates at the last minute
        // means the shop either eats the cost or delays the job.
        int plates = InkPreflight.CountPlates(
            new[] { Brand, new SpotColor("PANTONE 877 C", 0, 0, 0, 0.3) }, usesProcess: true);

        Assert.Equal(6, plates);
    }

    [Fact]
    public void TheSameInkTwiceIsStillOnePlate()
    {
        int plates = InkPreflight.CountPlates(
            new[] { Brand, Brand with { } }, usesProcess: false);

        Assert.Equal(1, plates);
    }

    [Fact]
    public void TechnicalInksAreNotCountedAsColourPlates()
    {
        // A die line is not an ink the press lays down.
        int plates = InkPreflight.CountPlates(new[] { Brand, DieLine }, usesProcess: true);

        Assert.Equal(5, plates);
    }

    // ── Undefined spots ──────────────────────────────────────────────────────

    [Fact]
    public void AnInkNamedButNotDefinedIsReported()
    {
        // Undefined spots fall back to a process approximation — the customer's brand
        // colour printed as "close enough", which is the failure spot inks exist to
        // prevent.
        var missing = InkPreflight.FindUndefinedSpots(
            new[] { "PANTONE 185 C", "PANTONE 032 C" }, new[] { Brand });

        Assert.Single(missing);
        Assert.Equal("PANTONE 032 C", missing[0]);
    }

    [Fact]
    public void InkNamesMatchIgnoringCaseAndStraySpaces()
    {
        var missing = InkPreflight.FindUndefinedSpots(
            new[] { "  pantone 185 c  " }, new[] { Brand });

        Assert.Empty(missing);
    }

    // ── Overprint rules ──────────────────────────────────────────────────────

    [Fact]
    public void SmallBlackTextKnockingOutOfColourIsFlagged()
    {
        // The single most common overprint mistake. Knockout leaves a white outline
        // the moment the press drifts.
        var issue = InkPreflight.CheckBlackTextOverprint(
            fontSizePoints: 8, isSolidBlack: true,
            mode: OverprintMode.Knockout, onColouredBackground: true);

        Assert.NotNull(issue);
    }

    [Fact]
    public void TheSameTextSetToOverprintIsFine()
    {
        var issue = InkPreflight.CheckBlackTextOverprint(
            8, true, OverprintMode.Overprint, onColouredBackground: true);

        Assert.Null(issue);
    }

    [Fact]
    public void LargeBlackTextIsNotFlagged()
    {
        // A registration slip is invisible on a big headline; warning about it would
        // just train the operator to ignore warnings.
        var issue = InkPreflight.CheckBlackTextOverprint(
            48, true, OverprintMode.Knockout, onColouredBackground: true);

        Assert.Null(issue);
    }

    [Fact]
    public void BlackTextOnWhitePaperIsNotFlagged()
    {
        var issue = InkPreflight.CheckBlackTextOverprint(
            8, true, OverprintMode.Knockout, onColouredBackground: false);

        Assert.Null(issue);
    }

    [Fact]
    public void ALightColourSetToOverprintIsFlagged()
    {
        // Light ink cannot cover; it mixes with what is underneath and the result does
        // not match the proof.
        var issue = InkPreflight.CheckLightOverprint(
            ((byte)20, (byte)10, (byte)5, (byte)0), OverprintMode.Overprint,
            onColouredBackground: true);

        Assert.NotNull(issue);
    }

    [Fact]
    public void ADenseColourSetToOverprintIsNotFlagged()
    {
        var issue = InkPreflight.CheckLightOverprint(
            ((byte)200, (byte)180, (byte)160, (byte)255), OverprintMode.Overprint,
            onColouredBackground: true);

        Assert.Null(issue);
    }

    // ── Technical inks ───────────────────────────────────────────────────────

    [Fact]
    public void TechnicalInksAreCalledOutForSeparation()
    {
        var issues = InkPreflight.CheckTechnicalInks(new List<SpotColor> { Brand, DieLine });

        Assert.Single(issues);
        Assert.Contains("CutContour", issues[0].Message);
    }

    // ── Preview ──────────────────────────────────────────────────────────────

    [Fact]
    public void ATintPreviewScalesTheAlternateValues()
    {
        var half = Brand.PreviewAt(0.5);

        Assert.Equal(0, half.C);
        Assert.Equal((byte)System.Math.Round(0.91 * 0.5 * 255), half.M);
    }

    [Fact]
    public void TintIsClampedToTheValidRange()
    {
        Assert.Equal(Brand.PreviewAt(1.0), Brand.PreviewAt(5.0));
        Assert.Equal(Brand.PreviewAt(0.0), Brand.PreviewAt(-3.0));
    }
}
