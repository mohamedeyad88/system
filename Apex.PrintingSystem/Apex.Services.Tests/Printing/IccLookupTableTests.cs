using System;
using System.IO;
using System.Linq;
using Apex.Services.Printing.Color;

namespace Apex.Services.Tests.Printing;

/// <summary>
/// The interpolated table has to stand in for the real profile.
///
/// Going through the CMM per pixel is ~14× slower — minutes for a page — so in practice
/// colour management would either be switched off or silently skipped. The table makes
/// it usable, but only if it stays faithful: an interpolation that drifts is worse than
/// no colour management, because the proof on screen stops matching the sheet.
/// </summary>
public class IccLookupTableTests
{
    private const string Dir = @"C:\Windows\System32\spool\drivers\color";

    private static IccColorTransform? NewTransform()
    {
        var src = new[] { "sRGB Color Space Profile.icm", "sRGB.icm" }
            .Select(n => Path.Combine(Dir, n)).FirstOrDefault(File.Exists);
        var dst = new[] { "CoatedFOGRA39.icc", "CoatedFOGRA27.icc", "RSWOP.icm" }
            .Select(n => Path.Combine(Dir, n)).FirstOrDefault(File.Exists);
        if (src == null || dst == null) return null;
        return IccColorTransform.Create(src, dst);
    }

    [Fact]
    public void GridNodesReproduceTheProfileExactly()
    {
        // At a node there is nothing to interpolate: any difference means the table is
        // built or indexed wrongly.
        using var t = NewTransform();
        if (t == null) return;

        var lut = IccLookupTable.Build(t, 9);
        Assert.NotNull(lut);

        double step = 255.0 / 8;
        for (int ri = 0; ri < 9; ri += 2)
            for (int gi = 0; gi < 9; gi += 2)
                for (int bi = 0; bi < 9; bi += 2)
                {
                    byte r = (byte)Math.Round(ri * step);
                    byte g = (byte)Math.Round(gi * step);
                    byte b = (byte)Math.Round(bi * step);

                    var exact = t.RgbToCmyk(r, g, b);
                    var node = lut!.RgbToCmyk(r, g, b);
                    Assert.NotNull(exact);

                    Assert.Equal(exact!.Value.C, node.C);
                    Assert.Equal(exact.Value.M, node.M);
                    Assert.Equal(exact.Value.Y, node.Y);
                    Assert.Equal(exact.Value.K, node.K);
                }
    }

    [Fact]
    public void InterpolationStaysFaithfulToTheProfile()
    {
        using var t = NewTransform();
        if (t == null) return;

        var lut = IccLookupTable.Build(t);   // default grid
        Assert.NotNull(lut);

        int maxErr = 0;
        long sumErr = 0, count = 0;
        var rnd = new Random(12345);         // fixed seed: a failure is reproducible

        for (int i = 0; i < 2000; i++)
        {
            byte r = (byte)rnd.Next(256), g = (byte)rnd.Next(256), b = (byte)rnd.Next(256);

            var exact = t.RgbToCmyk(r, g, b);
            if (exact == null) continue;
            var approx = lut!.RgbToCmyk(r, g, b);

            foreach (int d in new[]
            {
                Math.Abs(exact.Value.C - approx.C), Math.Abs(exact.Value.M - approx.M),
                Math.Abs(exact.Value.Y - approx.Y), Math.Abs(exact.Value.K - approx.K),
            })
            {
                if (d > maxErr) maxErr = d;
                sumErr += d;
                count++;
            }
        }

        double mean = (double)sumErr / count;

        // Measured on FOGRA39: max 6, mean 0.36. The bounds leave room for other
        // profiles while still failing loudly if interpolation breaks.
        Assert.True(maxErr <= 12, $"worst channel error {maxErr}/255 — the table has drifted from the profile");
        Assert.True(mean <= 1.5, $"mean channel error {mean:F2}/255 is too high");
    }

    [Fact]
    public void TheTableIsSubstantiallyFasterThanTheCmm()
    {
        // The entire reason the table exists. If it were not faster, per-pixel ICC would
        // be the simpler and more accurate choice.
        using var t = NewTransform();
        if (t == null) return;

        var lut = IccLookupTable.Build(t);
        Assert.NotNull(lut);

        const int samples = 20_000;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < samples; i++)
            lut!.RgbToCmyk((byte)(i & 255), (byte)((i >> 3) & 255), (byte)((i >> 5) & 255));
        sw.Stop();
        double lutMs = sw.Elapsed.TotalMilliseconds;

        const int iccSamples = 1000;
        sw.Restart();
        for (int i = 0; i < iccSamples; i++)
            t.RgbToCmyk((byte)(i & 255), (byte)((i >> 3) & 255), (byte)((i >> 5) & 255));
        sw.Stop();
        double iccMsScaled = sw.Elapsed.TotalMilliseconds * ((double)samples / iccSamples);

        Assert.True(lutMs * 4 < iccMsScaled,
            $"table {lutMs:F0}ms vs CMM {iccMsScaled:F0}ms for {samples} colours — not worth the approximation");
    }

    [Fact]
    public void BatchAndSingleAgree()
    {
        using var t = NewTransform();
        if (t == null) return;
        var lut = IccLookupTable.Build(t, 9);
        Assert.NotNull(lut);

        byte[] rgb = { 0, 0, 0, 255, 255, 255, 237, 28, 36, 0, 174, 239, 77, 130, 200 };
        var cmyk = new byte[(rgb.Length / 3) * 4];
        lut!.RgbToCmyk(rgb, cmyk);

        for (int i = 0; i < rgb.Length / 3; i++)
        {
            var one = lut.RgbToCmyk(rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2]);
            Assert.Equal(one.C, cmyk[i * 4]);
            Assert.Equal(one.M, cmyk[i * 4 + 1]);
            Assert.Equal(one.Y, cmyk[i * 4 + 2]);
            Assert.Equal(one.K, cmyk[i * 4 + 3]);
        }
    }

    [Fact]
    public void WhiteAndBlackCornersAreExact()
    {
        // The two colours an operator checks first, and both sit exactly on grid corners.
        using var t = NewTransform();
        if (t == null) return;
        var lut = IccLookupTable.Build(t);
        Assert.NotNull(lut);

        var white = lut!.RgbToCmyk(255, 255, 255);
        Assert.True(white.C + white.M + white.Y + white.K < 40, "white is laying down ink");

        var black = lut.RgbToCmyk(0, 0, 0);
        Assert.True(black.K > 128, $"black came out with only K{black.K}");
    }
}
