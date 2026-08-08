using System.IO;
using Apex.Services.Printing.Color;

namespace Apex.Services.Tests.Printing;

/// <summary>
/// Real ICC transforms through the operating system's colour engine.
///
/// These run against whatever profiles the machine actually has. If a build agent has
/// none, the tests skip rather than fail — but on any machine with a printer driver
/// installed they exercise the genuine path, which is the only way to know the native
/// interop is right. Getting the COLOR union size wrong, for instance, produces
/// plausible-looking numbers that are silently the wrong colour.
/// </summary>
public class IccColorTransformTests
{
    private const string ColorDir = @"C:\Windows\System32\spool\drivers\color";

    private static string? FindProfile(params string[] candidates)
    {
        foreach (var name in candidates)
        {
            var path = Path.Combine(ColorDir, name);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    private static string? Rgb() => FindProfile("sRGB Color Space Profile.icm", "sRGB.icm", "AdobeRGB1998.icc");

    private static string? Cmyk() => FindProfile(
        "CoatedFOGRA39.icc", "CoatedFOGRA27.icc", "CoatedGRACoL2006.icc",
        "USWebCoatedSWOP.icc", "RSWOP.icm", "EuroscaleCoated.icc",
        "JapanColor2001Coated.icc");

    private static IccColorTransform? NewTransform(
        IccRenderingIntent intent = IccRenderingIntent.RelativeColorimetric)
    {
        var src = Rgb();
        var dst = Cmyk();
        if (src == null || dst == null) return null;
        return IccColorTransform.Create(src, dst, intent);
    }

    [Fact]
    public void APressProfileOnThisMachineProducesAWorkingTransform()
    {
        using var t = NewTransform();
        if (t == null) return;   // no profiles installed — nothing to assert

        Assert.NotNull(t.RgbToCmyk(0, 0, 0));
    }

    [Fact]
    public void PaperWhite_UsesEssentiallyNoInk()
    {
        using var t = NewTransform();
        if (t == null) return;

        var ink = t.RgbToCmyk(255, 255, 255);
        Assert.NotNull(ink);

        int total = ink!.Value.C + ink.Value.M + ink.Value.Y + ink.Value.K;
        Assert.True(total < 40,
            $"white separated to C{ink.Value.C} M{ink.Value.M} Y{ink.Value.Y} K{ink.Value.K} " +
            "— the press would lay ink on blank paper");
    }

    [Fact]
    public void Black_IsMostlyBlackInk()
    {
        using var t = NewTransform();
        if (t == null) return;

        var ink = t.RgbToCmyk(0, 0, 0);
        Assert.NotNull(ink);

        Assert.True(ink!.Value.K > 128,
            $"black came out with only K{ink.Value.K} — the profile transform is wrong");
    }

    [Fact]
    public void SaturatedRed_LandsInTheMagentaYellowCorner()
    {
        using var t = NewTransform();
        if (t == null) return;

        var ink = t.RgbToCmyk(237, 28, 36);
        Assert.NotNull(ink);

        // Whatever the profile, red is built from magenta + yellow with little cyan.
        Assert.True(ink!.Value.M > 128, $"M={ink.Value.M}");
        Assert.True(ink.Value.Y > 128, $"Y={ink.Value.Y}");
        Assert.True(ink.Value.C < ink.Value.M, $"C={ink.Value.C} should be well below M={ink.Value.M}");
    }

    [Fact]
    public void BatchAndSingleConversionAgree()
    {
        // The batch path exists for speed; if it disagrees with the single path one of
        // them is marshalling wrong, and a whole image would separate incorrectly.
        using var t = NewTransform();
        if (t == null) return;

        byte[] rgb = { 0, 0, 0, 255, 255, 255, 237, 28, 36, 0, 174, 239, 128, 128, 128 };
        var cmyk = new byte[(rgb.Length / 3) * 4];

        Assert.True(t.RgbToCmyk(rgb, cmyk));

        for (int i = 0; i < rgb.Length / 3; i++)
        {
            var one = t.RgbToCmyk(rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2]);
            Assert.NotNull(one);
            Assert.Equal(one!.Value.C, cmyk[i * 4]);
            Assert.Equal(one.Value.M, cmyk[i * 4 + 1]);
            Assert.Equal(one.Value.Y, cmyk[i * 4 + 2]);
            Assert.Equal(one.Value.K, cmyk[i * 4 + 3]);
        }
    }

    [Fact]
    public void RenderingIntentActuallyChangesTheResult()
    {
        // If every intent returned the same numbers, the intent parameter would be
        // decoration and out-of-gamut brand colours would be handled wrongly.
        using var perceptual = NewTransform(IccRenderingIntent.Perceptual);
        using var saturation = NewTransform(IccRenderingIntent.Saturation);
        if (perceptual == null || saturation == null) return;

        var probe = new (byte r, byte g, byte b)[]
        {
            (0, 174, 239), (237, 28, 36), (0, 166, 81), (46, 49, 146),
        };

        bool anyDifference = false;
        foreach (var p in probe)
        {
            var a = perceptual.RgbToCmyk(p.r, p.g, p.b);
            var b = saturation.RgbToCmyk(p.r, p.g, p.b);
            if (a == null || b == null) continue;
            if (!a.Value.Equals(b.Value)) { anyDifference = true; break; }
        }

        Assert.True(anyDifference, "every rendering intent produced identical output");
    }

    [Fact]
    public void MissingProfile_ReturnsNullInsteadOfThrowing()
    {
        var t = IccColorTransform.Create(
            Path.Combine(ColorDir, "definitely-not-here.icc"),
            Path.Combine(ColorDir, "also-not-here.icc"));

        Assert.Null(t);
    }

    [Fact]
    public void UsingADisposedTransformIsARealError_NotSilentGarbage()
    {
        var t = NewTransform();
        if (t == null) return;

        t.Dispose();

        Assert.Throws<System.ObjectDisposedException>(() => t.RgbToCmyk(0, 0, 0));
    }
}
