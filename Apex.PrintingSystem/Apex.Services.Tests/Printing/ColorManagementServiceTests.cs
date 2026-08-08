using Apex.Services.Printing.Color;

namespace Apex.Services.Tests.Printing;

/// <summary>
/// Which profile a job is converted against, and why.
///
/// "Convert to CMYK" is only half an instruction — CMYK belongs to a device. The order
/// of preference here is a business decision, and the one rule that matters most is
/// negative: a printer with no profile of its own must never borrow another printer's.
/// That is wrong with confidence, and nothing on screen would say so.
/// </summary>
public class ColorManagementServiceTests
{
    private static ColorManagementService NewService() => new();

    [Fact]
    public void AnUnknownPrinter_FallsBackToTheShopStandard_NotToAStrangersProfile()
    {
        var svc = NewService();
        if (svc.DefaultPressProfilePath == null || svc.SourceRgbProfilePath == null) return;

        var r = svc.Resolve("A Printer That Does Not Exist 9999");

        Assert.Equal(ColorPath.ShopDefaultProfile, r.Path);
        Assert.Equal(svc.DefaultPressProfilePath, r.ProfilePath);
    }

    [Fact]
    public void WithNoProfilesAtAll_ItSaysSo_AndStillConverts()
    {
        var svc = NewService();
        svc.SourceRgbProfilePath = null;
        svc.DefaultPressProfilePath = null;

        var r = svc.Resolve("anything");
        Assert.Equal(ColorPath.Formula, r.Path);

        // The run must still produce ink rather than fail.
        var ink = svc.RgbToCmyk(0, 0, 0, "anything");
        Assert.Equal(255, ink.K);
    }

    [Fact]
    public void TheFormulaFallbackStillRespectsTheInkLimit()
    {
        var svc = NewService();
        svc.SourceRgbProfilePath = null;
        svc.DefaultPressProfilePath = null;
        svc.Separation = CmykSeparationSettings.Newsprint;

        double worst = 0;
        for (int r = 0; r <= 255; r += 25)
            for (int g = 0; g <= 255; g += 25)
                for (int b = 0; b <= 255; b += 25)
                {
                    var ink = svc.RgbToCmyk((byte)r, (byte)g, (byte)b, "press");
                    worst = System.Math.Max(worst, (ink.C + ink.M + ink.Y + ink.K) / 255.0);
                }

        Assert.True(worst <= CmykSeparationSettings.Newsprint.TotalAreaCoverage + 0.01,
            $"{worst * 100:F0}% exceeds the newsprint limit");
    }

    [Fact]
    public void AProfiledPathProducesDifferentInkThanTheFormula()
    {
        // If both routes agreed, the profile would not be doing anything and the whole
        // feature would be decoration.
        var profiled = NewService();
        if (profiled.DefaultPressProfilePath == null || profiled.SourceRgbProfilePath == null) return;

        var plain = NewService();
        plain.SourceRgbProfilePath = null;
        plain.DefaultPressProfilePath = null;

        var probes = new (byte r, byte g, byte b)[]
        {
            (0, 0, 0), (237, 28, 36), (0, 174, 239), (128, 128, 128),
        };

        bool anyDifference = probes.Any(p =>
            !profiled.RgbToCmyk(p.r, p.g, p.b, "press").Equals(
              plain.RgbToCmyk(p.r, p.g, p.b, "press")));

        Assert.True(anyDifference, "the ICC path returned exactly what the formula returns");
    }

    [Fact]
    public void ChangingTheShopStandardChangesTheOutput()
    {
        // The setting has to actually reach the press, otherwise a shop on uncoated
        // stock would silently keep printing to a coated standard.
        var a = NewService();
        var b = NewService();
        if (a.DefaultPressProfilePath == null || a.SourceRgbProfilePath == null) return;

        var alternative = System.IO.Directory
            .EnumerateFiles(System.IO.Path.GetDirectoryName(a.DefaultPressProfilePath)!, "*.icc")
            .FirstOrDefault(f => !string.Equals(f, a.DefaultPressProfilePath, System.StringComparison.OrdinalIgnoreCase)
                              && System.IO.Path.GetFileName(f).Contains("Uncoated", System.StringComparison.OrdinalIgnoreCase));
        if (alternative == null) return;   // machine has no second profile to compare

        b.DefaultPressProfilePath = alternative;

        var one = a.RgbToCmyk(128, 128, 128, "press");
        var two = b.RgbToCmyk(128, 128, 128, "press");

        Assert.NotEqual(one, two);
    }

    [Fact]
    public void ResolutionExplainsItself()
    {
        // The operator has to be able to see which profile is in play before the run.
        var svc = NewService();

        var r = svc.Resolve("EPSON WF-C5210 Series");

        Assert.False(string.IsNullOrWhiteSpace(r.Reason));
    }
}
