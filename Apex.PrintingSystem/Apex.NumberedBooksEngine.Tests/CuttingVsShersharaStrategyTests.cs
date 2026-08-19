using Apex.NumberedBooksEngine.Core;
using Apex.NumberedBooksEngine.Models;

namespace Apex.NumberedBooksEngine.Tests;

/// <summary>
/// Proves the two numbering strategies actually diverge — the comparison the field
/// test on 2026-08-16 could not make, because it used a single numbering slot.
///
/// With one slot the imposition formula (start + pageIndex + slotIndex*totalPages,
/// slotIndex always 0) collapses to plain sequential, so Cutting and Shershara are
/// identical by construction. Reporting "Cutting is inert" was wrong; it simply was
/// never exercised. The mode only means anything with two or more slots per sheet.
/// </summary>
public class CuttingVsShersharaStrategyTests
{
    private static BookJobOptions Job(int slots, long start, long total) => new(
        TemplateStream: null,
        TemplatePath: "t",
        TemplateFormat: TemplateFormat.Image,
        Layout: LayoutSpec.A4,
        Slots: Enumerable.Range(0, slots).Select(i =>
            new SlotSpec($"S{i}", 0, 0, 0, 0, "Arial", 10, "#000000", TextAlign.Left, 0, null)).ToList(),
        StartNumber: start,
        TotalNumbers: total,
        PagesPerBook: 1,
        CopiesPerPage: 1,
        // The strategies read only Slots/StartNumber/TotalNumbers; the rest are
        // required by the record but irrelevant to page-number generation.
        Mode: NumberingMode.Auto,
        LowResourceMode: false,
        DegreeOfParallelism: 1,
        CheckpointEvery: 100,
        OutputMode: "DirectPrint",
        OutputPath: "");

    /// <summary>
    /// Shershara fills each sheet top-to-bottom, so a stack of cut sheets does NOT
    /// run in sequence: sheet 0 is [1,2,3,4], sheet 1 is [5,6,7,8].
    /// </summary>
    [Fact]
    public void Shershara_FillsEachSheetSequentially()
    {
        var pages = new ShersharaStrategy().GeneratePageNumbers(Job(4, 1, 8)).ToList();

        Assert.Equal(2, pages.Count);
        Assert.Equal(new long[] { 1, 2, 3, 4 }, pages[0]);
        Assert.Equal(new long[] { 5, 6, 7, 8 }, pages[1]);
    }

    /// <summary>
    /// Cutting staggers the numbers so that after the sheets are guillotined and the
    /// piles stacked, each pile runs 1,2,3…: slot 0 pile = 1,2; slot 1 pile = 3,4; etc.
    /// Sheet 0 is [1,3,5,7], sheet 1 is [2,4,6,8].
    /// </summary>
    [Fact]
    public void Cutting_StaggersSoEachCutPileRunsInSequence()
    {
        var pages = new CuttingStrategy().GeneratePageNumbers(Job(4, 1, 8)).ToList();

        Assert.Equal(2, pages.Count);
        Assert.Equal(new long[] { 1, 3, 5, 7 }, pages[0]);
        Assert.Equal(new long[] { 2, 4, 6, 8 }, pages[1]);

        // The whole point: stacking pile-by-pile gives a continuous run.
        long[] slot0Pile = pages.Select(p => p[0]).ToArray();
        Assert.Equal(new long[] { 1, 2 }, slot0Pile);
    }

    /// <summary>
    /// The two modes must produce different sheets with several slots — otherwise the
    /// selector is decorative. (They coincide only at one slot, verified below.)
    /// </summary>
    [Fact]
    public void TheTwoModesDifferWithSeveralSlots()
    {
        var shershara = new ShersharaStrategy().GeneratePageNumbers(Job(4, 1, 8)).ToList();
        var cutting = new CuttingStrategy().GeneratePageNumbers(Job(4, 1, 8)).ToList();

        Assert.NotEqual(shershara[0], cutting[0]);
    }

    /// <summary>
    /// And the record of why the field test was inconclusive: with one slot they are
    /// identical, so identical output there was correct, not a defect.
    /// </summary>
    [Fact]
    public void WithASingleSlot_TheTwoModesCoincide()
    {
        var shershara = new ShersharaStrategy().GeneratePageNumbers(Job(1, 1, 4)).ToList();
        var cutting = new CuttingStrategy().GeneratePageNumbers(Job(1, 1, 4)).ToList();

        Assert.Equal(shershara.Select(p => p[0]), cutting.Select(p => p[0]));
    }
}
