using System.Collections.Generic;
using Apex.NumberedBooksEngine.Models;
using Apex.Services.Numbering;
using Xunit;

namespace Apex.Services.Tests.Numbering;

/// <summary>
/// The preview has to number a sheet the way the press will.
///
/// Each previewed sheet used to be composed with a single number, so on a design with
/// several fields only the first was numbered and the rest came out blank; it also
/// ignored cutting mode. An operator checks the preview before committing the paper,
/// and with more than one field it had never shown what would print.
/// </summary>
public class PreviewPageNumbersTests
{
    private static List<SlotSpec> Fields(int n)
    {
        var list = new List<SlotSpec>();
        for (int i = 0; i < n; i++)
            list.Add(new SlotSpec($"s{i}", 0.1f, 0.1f * i, 0.3f, 0.08f, "Arial", 24f, "#000000", TextAlign.Center, 0f, null));
        return list;
    }

    [Fact]
    public void EveryFieldOnTheSheetGetsANumber()
    {
        var opts = NumberingService.PreviewJobOptions(Fields(5), startNumber: 1, pageCount: 1,
            totalNumbers: 100, mode: NumberingMode.Linear);

        var pages = NumberingService.PreviewPageNumbers(opts, 1);

        Assert.Single(pages);
        Assert.Equal(new long[] { 1, 2, 3, 4, 5 }, pages[0]);
    }

    [Fact]
    public void SequentialPagesContinueWhereTheLastSheetStopped()
    {
        var opts = NumberingService.PreviewJobOptions(Fields(2), startNumber: 1, pageCount: 4,
            totalNumbers: 100, mode: NumberingMode.Linear);

        var pages = NumberingService.PreviewPageNumbers(opts, 4);

        Assert.Equal(4, pages.Count);
        Assert.Equal(new long[] { 1, 2 }, pages[0]);
        Assert.Equal(new long[] { 7, 8 }, pages[3]);
    }

    /// <summary>
    /// Cutting mode depends on the whole job's count: 100 numbers over 3 fields is 34
    /// sheets, so the first sheet carries 1, 35 and 69. Previewing with a smaller count
    /// would show different — wrong — numbers.
    /// </summary>
    [Fact]
    public void CuttingModeUsesTheWholeJobToPlaceEachPile()
    {
        var opts = NumberingService.PreviewJobOptions(Fields(3), startNumber: 1, pageCount: 1,
            totalNumbers: 100, mode: NumberingMode.Imposed);

        var pages = NumberingService.PreviewPageNumbers(opts, 1);

        Assert.Equal(new long[] { 1, 35, 69 }, pages[0]);
    }

    [Fact]
    public void AShortRangeLeavesTheUnreachedFieldsEmpty()
    {
        var opts = NumberingService.PreviewJobOptions(Fields(4), startNumber: 1, pageCount: 1,
            totalNumbers: 2, mode: NumberingMode.Linear);

        var pages = NumberingService.PreviewPageNumbers(opts, 1);

        Assert.Equal(new long[] { 1, 2, -1, -1 }, pages[0]);
    }
}
