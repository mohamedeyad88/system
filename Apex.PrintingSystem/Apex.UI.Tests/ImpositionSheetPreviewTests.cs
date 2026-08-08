using System.Collections.ObjectModel;
using System.Windows;
using Apex.UI.Controls;
using Apex.UI.ViewModels;

namespace Apex.UI.Tests;

/// <summary>
/// Guards the property that makes the imposition preview trustworthy: it must show
/// the sheet at its TRUE aspect ratio. The previous implementation stretched slot
/// ratios into a fixed 420×300 canvas, so a portrait SRA3 sheet was drawn
/// landscape — a preview that contradicts the press sheet is worse than none.
/// </summary>
public class ImpositionSheetPreviewTests
{
    /// <summary>
    /// WPF elements can only be constructed on an STA thread, and xUnit runs tests
    /// on MTA — so the measure pass is marshalled onto a dedicated STA thread.
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

    /// <summary>Measures the control and returns its DesiredSize.</summary>
    private static Size Sized(double wMm, double hMm, Size available) => RunSta(() =>
    {
        var c = new ImpositionSheetPreview { SheetWidthMm = wMm, SheetHeightMm = hMm };
        c.Slots = new ObservableCollection<ImpositionPreviewRect>
        {
            new() { XMm = 0, YMm = 0, WidthMm = wMm / 2, HeightMm = hMm / 2, PageLabel = "1" },
        };
        c.Measure(available);
        return c.DesiredSize;
    });

    [Fact]
    public void PortraitSheet_StaysPortrait_InALandscapeBox()
    {
        // SRA3 320×450 is portrait; the available box is landscape.
        var size = Sized(320, 450, new Size(420, 300));

        Assert.True(size.Height > size.Width,
            $"portrait sheet must stay portrait, got {size}");
    }

    [Fact]
    public void LandscapeSheet_StaysLandscape_InAPortraitBox()
    {
        var size = Sized(450, 320, new Size(300, 420));

        Assert.True(size.Width > size.Height,
            $"landscape sheet must stay landscape, got {size}");
    }

    [Theory]
    [InlineData(320, 450)]   // SRA3 portrait
    [InlineData(450, 320)]   // SRA3 landscape
    [InlineData(210, 297)]   // A4
    [InlineData(1000, 700)]  // wide-format
    public void AspectRatioIsPreservedExactly(double wMm, double hMm)
    {
        var size = Sized(wMm, hMm, new Size(500, 400));

        double expected = wMm / hMm;
        double actual = size.Width / size.Height;
        Assert.Equal(expected, actual, 6);
    }

    [Fact]
    public void FitsInsideTheAvailableBox()
    {
        var available = new Size(420, 300);
        var size = Sized(320, 450, available);

        Assert.True(size.Width <= available.Width + 0.001);
        Assert.True(size.Height <= available.Height + 0.001);
    }

    [Fact]
    public void WithoutAPlan_StillOccupiesSpaceSoTheHintIsVisible()
    {
        var size = RunSta(() =>
        {
            var c = new ImpositionSheetPreview { EmptyHint = "hint" };
            c.Measure(new Size(400, 300));
            return c.DesiredSize;
        });

        Assert.Equal(400, size.Width, 3);
        Assert.Equal(300, size.Height, 3);
    }

    [Fact]
    public void InfiniteAvailableSize_FallsBackToABoundedSize()
    {
        var size = Sized(320, 450, new Size(double.PositiveInfinity, double.PositiveInfinity));

        Assert.True(size.Width > 0 && !double.IsInfinity(size.Width));
        Assert.True(size.Height > 0 && !double.IsInfinity(size.Height));
        Assert.Equal(320.0 / 450.0, size.Width / size.Height, 6);
    }

    [Fact]
    public void ZeroAvailableSize_DoesNotThrow()
    {
        var size = Sized(320, 450, new Size(0, 0));
        Assert.Equal(0, size.Width);
        Assert.Equal(0, size.Height);
    }

    // ── Slot model ────────────────────────────────────────────────────────────

    [Fact]
    public void BlankSlot_IsFlaggedBlank()
    {
        var slot = new ImpositionPreviewRect { PageLabel = "" };
        Assert.True(slot.IsBlank);
        Assert.False(slot.IsRotated);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(90, true)]
    [InlineData(180, true)]
    [InlineData(270, true)]
    public void RotationFlag(int degrees, bool expected)
    {
        Assert.Equal(expected, new ImpositionPreviewRect { Rotation = degrees }.IsRotated);
    }
}
