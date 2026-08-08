using Apex.Services.Printing.Color;

// Deliberately NOT namespaced ...Tests.Color: that shadows System.Drawing.Color
// for every other test file in the assembly.
namespace Apex.Services.Tests.Printing;

/// <summary>
/// Total Area Coverage — the sum of all four inks at one point.
///
/// This is the number that decides whether a sheet dries. Exceed what the press and
/// paper hold and the ink sets off onto the next sheet, picks in the nip, and the run
/// is scrap. The separation used to multiply black by 0.85 and describe that as a 320%
/// cap; it is the opposite — shrinking K pushes ink back into C, M and Y, and solid
/// black came out at C100 M100 Y100 K85 = 385%.
/// </summary>
public class InkLimitTests
{
    private static double Tac((byte C, byte M, byte Y, byte K) ink) =>
        (ink.C + ink.M + ink.Y + ink.K) / 255.0;

    [Fact]
    public void SolidBlack_IsNotFourInks()
    {
        var ink = ColorTransformEngine.RgbToCmyk(0, 0, 0);

        Assert.Equal(255, ink.K);
        Assert.Equal(0, ink.C);
        Assert.Equal(0, ink.M);
        Assert.Equal(0, ink.Y);
    }

    [Fact]
    public void SolidBlack_UsedToBlowTheInkLimit()
    {
        // The regression this whole class exists for: 385% total.
        var ink = ColorTransformEngine.RgbToCmyk(0, 0, 0);

        Assert.True(Tac(ink) <= 3.00 + 0.01,
            $"solid black separated to {Tac(ink) * 100:F0}% ink — the sheet will not dry");
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 0, 60)]
    [InlineData(10, 5, 40)]
    [InlineData(30, 30, 30)]
    [InlineData(0, 40, 80)]
    [InlineData(60, 0, 0)]
    [InlineData(20, 60, 10)]
    public void NoColourExceedsTheDefaultLimit(byte r, byte g, byte b)
    {
        var ink = ColorTransformEngine.RgbToCmyk(r, g, b);

        Assert.True(Tac(ink) <= 3.00 + 0.01,
            $"rgb({r},{g},{b}) → {Tac(ink) * 100:F0}% ink, limit 300%");
    }

    [Fact]
    public void NoColourInTheWholeCubeExceedsTheLimit()
    {
        // Sampling the cube is what catches the dark saturated corners a
        // hand-picked list misses.
        var settings = CmykSeparationSettings.Default;
        double worst = 0;
        (int r, int g, int b) worstAt = default;

        for (int r = 0; r <= 255; r += 15)
            for (int g = 0; g <= 255; g += 15)
                for (int b = 0; b <= 255; b += 15)
                {
                    double tac = Tac(ColorTransformEngine.RgbToCmyk((byte)r, (byte)g, (byte)b, settings));
                    if (tac > worst) { worst = tac; worstAt = (r, g, b); }
                }

        Assert.True(worst <= settings.TotalAreaCoverage + 0.01,
            $"worst case rgb{worstAt} = {worst * 100:F0}% ink, limit {settings.TotalAreaCoverage * 100:F0}%");
    }

    [Theory]
    [InlineData(2.40)]   // newsprint
    [InlineData(2.80)]   // digital
    [InlineData(3.40)]   // coated sheetfed offset
    public void EachPressPresetIsHonoured(double limit)
    {
        var settings = new CmykSeparationSettings { TotalAreaCoverage = limit };
        double worst = 0;

        for (int r = 0; r <= 255; r += 25)
            for (int g = 0; g <= 255; g += 25)
                for (int b = 0; b <= 255; b += 25)
                    worst = System.Math.Max(worst,
                        Tac(ColorTransformEngine.RgbToCmyk((byte)r, (byte)g, (byte)b, settings)));

        Assert.True(worst <= limit + 0.01, $"worst {worst * 100:F0}% exceeds {limit * 100:F0}%");
    }

    [Fact]
    public void White_UsesNoInkAtAll()
    {
        var ink = ColorTransformEngine.RgbToCmyk(255, 255, 255);

        Assert.Equal(0, ink.C);
        Assert.Equal(0, ink.M);
        Assert.Equal(0, ink.Y);
        Assert.Equal(0, ink.K);
    }

    [Fact]
    public void LightGrey_StaysChromatic_SoItDoesNotBreakIntoBlackDots()
    {
        // Above BlackStart black has not kicked in yet: a pale grey must not be
        // rendered as scattered black dots on a smooth tint.
        var ink = ColorTransformEngine.RgbToCmyk(230, 230, 230);

        Assert.Equal(0, ink.K);
    }

    [Fact]
    public void BlackMaxCap_IsRespected()
    {
        var settings = new CmykSeparationSettings { BlackMax = 0.90, TotalAreaCoverage = 3.00 };

        var ink = ColorTransformEngine.RgbToCmyk(0, 0, 0, settings);

        Assert.True(ink.K <= (byte)System.Math.Round(0.90 * 255) + 1,
            $"K={ink.K} exceeds the 90% black cap");
    }
}
