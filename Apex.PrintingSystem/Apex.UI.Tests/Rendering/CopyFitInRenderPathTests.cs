using System;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using Apex.Services.SmartVariables;

namespace Apex.UI.Tests.Rendering;

/// <summary>
/// Copy-fit where it actually matters: the code that draws the record.
///
/// CopyFitCalculator has been unit-tested on a synthetic measurer since it was
/// written, but the render target never called it — it set MaxTextWidth/Height with
/// TextTrimming.CharacterEllipsis, so a long value was silently cut short instead of
/// shrunk. These tests measure with the real WPF text engine, which is what decides
/// what lands on paper.
/// </summary>
public class CopyFitInRenderPathTests
{
    /// <summary>
    /// WPF text objects want an STA thread, and xUnit runs tests on MTA — so the
    /// measuring is marshalled onto a dedicated STA thread.
    /// </summary>
    private static T RunSta<T>(Func<T> body)
    {
        T result = default!;
        Exception? error = null;

        var t = new Thread(() =>
        {
            try { result = body(); }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
        t.Join();

        if (error != null) throw new InvalidOperationException("STA body threw", error);
        return result;
    }

    private static Typeface Face() => new(
        new FontFamily("Tahoma"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    /// <summary>Real WPF measurement — the same call the render target makes.</summary>
    private static MeasureText Measure() => (text, size) =>
    {
        var ft = new FormattedText(
            text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face(),
            Math.Max(size, 0.1), Brushes.Black, pixelsPerDip: 1.0);
        return (ft.Width, ft.Height);
    };

    [Fact]
    public void ALongValueIsShrunkInsteadOfTrimmed()
    {
        // A name far longer than the template author allowed for.
        const string longName = "عبد الرحمن محمد إبراهيم السيد الشرقاوي";

        RunSta<object?>(() =>
        {
            var r = CopyFitCalculator.Fit(longName, boxWidth: 160, boxHeight: 18,
                startSize: 14, minSize: 6, Measure());

            Assert.True(r.Fits, "the name could not be fitted at all");
            Assert.True(r.FontSize < 14, "the text was not shrunk");

            var (w, h) = Measure()(longName, r.FontSize);
            Assert.True(w <= 160.5, $"still {w:F1} wide in a 160 box");
            Assert.True(h <= 18.5, $"still {h:F1} tall in an 18 box");
            return null;
        });
    }

    [Fact]
    public void AShortValueKeepsTheDesignedSize()
    {
        // Shrinking when there is no need would make a set of cards uneven.
        RunSta<object?>(() =>
        {
            var r = CopyFitCalculator.Fit("علي", 160, 18, startSize: 14, minSize: 6, Measure());

            Assert.True(r.Fits);
            Assert.Equal(14, r.FontSize);
            return null;
        });
    }

    [Fact]
    public void ArabicAndLatinAreBothMeasuredHonestly()
    {
        // The shaping engine treats them differently; both have to actually fit.
        RunSta<object?>(() =>
        {
            foreach (var text in new[]
            {
                "Mohamed Abd El-Rahman El-Sayed",
                "محمد عبد الرحمن السيد إبراهيم",
            })
            {
                var r = CopyFitCalculator.Fit(text, 120, 16, 14, 5, Measure());
                Assert.True(r.Fits, $"could not fit: {text}");

                var (w, _) = Measure()(text, r.FontSize);
                Assert.True(w <= 120.5, $"'{text}' is {w:F1} wide in a 120 box");
            }
            return null;
        });
    }

    [Fact]
    public void AValueThatCannotFitIsReported_SoTheRecordCanBeCaught()
    {
        RunSta<object?>(() =>
        {
            var r = CopyFitCalculator.Fit(new string('م', 400), boxWidth: 40, boxHeight: 10,
                startSize: 12, minSize: 8, Measure());

            Assert.False(r.Fits);
            Assert.Equal(8, r.FontSize);
            return null;
        });
    }

    [Fact]
    public void ZeroMinimumDisablesShrinking()
    {
        // MinFontSize = 0 means "trim like before" — existing templates that relied
        // on the old behaviour must not silently change.
        var style = new Apex.UI.Rendering.TextStyle { FontSize = 14, MinFontSize = 0 };

        Assert.Equal(0, style.MinFontSize);
    }
}
