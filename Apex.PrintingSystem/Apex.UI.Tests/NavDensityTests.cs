using Apex.UI.ViewModels;
using Xunit;

namespace Apex.UI.Tests;

/// <summary>
/// The sidebar carries ten sections with a description line each, plus group headings.
/// On a 1366×768 laptop — the smallest screen in these shops — Settings and Licensing
/// fell below the fold and could only be reached by scrolling the sidebar itself.
/// </summary>
public class NavDensityTests
{
    [Theory]
    [InlineData(768)]    // 1366×768 laptop
    [InlineData(800)]
    [InlineData(899)]
    public void ShortScreens_DropTheDescriptionLines(double height) =>
        Assert.True(MainViewModel.NavCompactFor(height));

    [Theory]
    [InlineData(900)]
    [InlineData(1040)]   // 1920×1080 with the taskbar
    [InlineData(1440)]
    public void TallScreens_KeepTheFullSidebar(double height) =>
        Assert.False(MainViewModel.NavCompactFor(height));

    [Fact]
    public void BeforeTheWindowHasASize_NothingIsCompacted() =>
        Assert.False(MainViewModel.NavCompactFor(0));
}
