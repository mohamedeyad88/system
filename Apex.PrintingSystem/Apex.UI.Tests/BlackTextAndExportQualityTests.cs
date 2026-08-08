using System.Windows.Media;
using Apex.UI.ViewModels;

namespace Apex.UI.Tests;

/// <summary>
/// Print-quality guards for the variable-data output.
///
/// Near-black text is snapped to pure black so a RIP maps it to K-only ink. An
/// off-black such as #1A1A1A separates into a four-colour "rich black", which on
/// small Arabic type shows coloured fringing wherever the press misregisters.
/// </summary>
public class BlackTextAndExportQualityTests
{
    private static Color BrushOf(string hex) =>
        new RenderedFieldItem { TextColor = hex }.TextBrush.Color;

    [Theory]
    [InlineData("#000000")]
    [InlineData("#010101")]
    [InlineData("#0A0A0A")]
    [InlineData("#181818")]   // exactly at the threshold
    public void NearBlack_IsSnappedToPureBlack(string hex)
    {
        var c = BrushOf(hex);

        Assert.Equal(0, c.R);
        Assert.Equal(0, c.G);
        Assert.Equal(0, c.B);
    }

    [Theory]
    [InlineData("#191919")]   // just past the threshold
    [InlineData("#333333")]
    [InlineData("#808080")]
    public void DarkGrey_IsLeftAlone_BecauseItIsADeliberateChoice(string hex)
    {
        var c = BrushOf(hex);
        Assert.True(c.R > 0, $"{hex} must not be forced to black");
    }

    [Fact]
    public void SaturatedColours_AreNeverAltered()
    {
        var red = BrushOf("#FF0000");
        Assert.Equal(255, red.R);
        Assert.Equal(0, red.G);
        Assert.Equal(0, red.B);

        // A very dark BLUE is not near-black on all channels, so it stays blue.
        var navy = BrushOf("#000080");
        Assert.Equal(0x80, navy.B);
    }

    [Fact]
    public void NearBlack_PreservesAlpha()
    {
        var c = BrushOf("#800A0A0A");
        Assert.Equal(0x80, c.A);
        Assert.Equal(0, c.R);
    }

    [Fact]
    public void InvalidColour_FallsBackToBlack()
    {
        Assert.Equal(Colors.Black, BrushOf("not-a-colour"));
    }
}
