using Apex.Core.Utilities;

namespace Apex.Services.Tests;

/// <summary>
/// Locks down the physical-unit maths. The bug these guard against is mixing
/// PDF points (1/72") with WPF DIPs (1/96"), which silently rescales printed
/// output to 75% — see <see cref="UnitConverter"/>.
/// </summary>
public class UnitConverterTests
{
    // A4 = 210 × 297 mm
    private const double A4WidthMm = 210.0;

    [Fact]
    public void MmToPoints_A4_Is595Points()
    {
        // 210mm = 8.2677in × 72 = 595.28pt (the standard A4 PDF width).
        Assert.Equal(595.28, UnitConverter.MmToPoints(A4WidthMm), 2);
    }

    [Fact]
    public void MmToDips_A4_Is794Dips()
    {
        // 210mm = 8.2677in × 96 = 793.70 DIP.
        Assert.Equal(793.70, UnitConverter.MmToDips(A4WidthMm), 2);
    }

    [Fact]
    public void PointsAndDips_DifferByExactly72Over96()
    {
        double pts = UnitConverter.MmToPoints(A4WidthMm);
        double dips = UnitConverter.MmToDips(A4WidthMm);

        // Using one where the other is expected scales output by 0.75.
        Assert.Equal(0.75, pts / dips, 6);
    }

    [Fact]
    public void MmToPixels_A4_At150Dpi_Is1240Pixels()
    {
        // This is the pixel width a 150-DPI A4 raster export must have to print
        // at true physical size.
        Assert.Equal(1240.16, UnitConverter.MmToPixels(A4WidthMm, 150), 2);
    }

    [Theory]
    [InlineData(72)]
    [InlineData(150)]
    [InlineData(300)]
    [InlineData(600)]
    public void MmToPixels_MatchesInchesTimesDpi(double dpi)
    {
        double expected = A4WidthMm / 25.4 * dpi;
        Assert.Equal(expected, UnitConverter.MmToPixels(A4WidthMm, dpi), 6);
    }

    [Fact]
    public void ExactConstants_AreNotTheRounded2Point835()
    {
        // The old magic constant 2.835 was a truncation of 72/25.4.
        Assert.Equal(72.0 / 25.4, UnitConverter.MmToPoint, 12);
        Assert.Equal(96.0 / 25.4, UnitConverter.MmToDip, 12);
        Assert.NotEqual(2.835, UnitConverter.MmToPoint);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(210)]
    [InlineData(1000)]
    public void MmToPoints_RoundTrips(double mm)
    {
        Assert.Equal(mm, UnitConverter.PointsToMm(UnitConverter.MmToPoints(mm)), 9);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(297)]
    [InlineData(1000)]
    public void MmToDips_RoundTrips(double mm)
    {
        Assert.Equal(mm, UnitConverter.DipsToMm(UnitConverter.MmToDips(mm)), 9);
    }

    [Fact]
    public void PointsToDips_RoundTrips()
    {
        double pts = UnitConverter.MmToPoints(A4WidthMm);
        Assert.Equal(pts, UnitConverter.DipsToPoints(UnitConverter.PointsToDips(pts)), 9);
    }

    [Fact]
    public void PixelsToMm_IsInverseOfMmToPixels()
    {
        double px = UnitConverter.MmToPixels(A4WidthMm, 300);
        Assert.Equal(A4WidthMm, UnitConverter.PixelsToMm(px, 300), 9);
    }

    [Fact]
    public void PixelsToMm_ZeroDpi_ReturnsZeroInsteadOfInfinity()
    {
        Assert.Equal(0, UnitConverter.PixelsToMm(1000, 0));
    }
}
