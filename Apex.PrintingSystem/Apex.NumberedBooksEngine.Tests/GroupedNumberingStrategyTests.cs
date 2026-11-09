using Apex.NumberedBooksEngine.Core;
using Apex.NumberedBooksEngine.Models;

namespace Apex.NumberedBooksEngine.Tests;

/// <summary>
/// Ticket/stub ("كعب") numbering: boxes that share a GroupId are ONE unit and must print the
/// SAME serial, while a sheet can hold several such tickets (booklets) each running its own
/// sequence. <see cref="GroupedNumberingStrategy"/> wraps any strategy to do this, and with no
/// groups it must be a transparent pass-through.
/// </summary>
public class GroupedNumberingStrategyTests
{
    // groupIds: one per slot (0 = ungrouped). Only Id/GroupId matter to the strategy.
    private static BookJobOptions Job(int[] groupIds, long start, long total, NumberingMode mode) => new(
        TemplateStream: null,
        TemplatePath: "t",
        TemplateFormat: TemplateFormat.Image,
        Layout: LayoutSpec.A4,
        Slots: groupIds.Select((g, i) =>
            new SlotSpec($"S{i}", 0, 0, 0, 0, "Arial", 10, "#000000", TextAlign.Left, 0, null, GroupId: g)).ToList(),
        StartNumber: start,
        TotalNumbers: total,
        PagesPerBook: 1,
        CopiesPerPage: 1,
        Mode: mode,
        LowResourceMode: false,
        DegreeOfParallelism: 1,
        CheckpointEvery: 100,
        OutputMode: "DirectPrint",
        OutputPath: "");

    private static GroupedNumberingStrategy Grouped(BookJobOptions job) =>
        new(NumberingStrategyFactory.Create(job), job.Slots);

    /// <summary>
    /// 3 booklets, each a stub+ticket pair (6 boxes, groups 1,1,2,2,3,3), cut-and-stack, 6
    /// tickets. Each pair prints one serial, and the three booklets stagger so each cut pile
    /// runs in sequence. Sheet 0 = [1,1,3,3,5,5], sheet 1 = [2,2,4,4,6,6].
    /// </summary>
    [Fact]
    public void StubAndTicket_ShareOneSerial_AcrossStackedBooklets()
    {
        var job = Job(new[] { 1, 1, 2, 2, 3, 3 }, start: 1, total: 6, NumberingMode.Imposed);
        var pages = Grouped(job).GeneratePageNumbers(job).ToList();

        Assert.Equal(2, pages.Count);
        Assert.Equal(new long[] { 1, 1, 3, 3, 5, 5 }, pages[0]);
        Assert.Equal(new long[] { 2, 2, 4, 4, 6, 6 }, pages[1]);

        // Within every sheet, each booklet's two boxes carry the identical number.
        foreach (var page in pages)
        {
            Assert.Equal(page[0], page[1]); // booklet 1: stub == ticket
            Assert.Equal(page[2], page[3]); // booklet 2
            Assert.Equal(page[4], page[5]); // booklet 3
        }

        // Booklet 1's pile stacks 1,2,…; booklet 2's 3,4,…; booklet 3's 5,6,… — real booklets.
        Assert.Equal(new long[] { 1, 2 }, pages.Select(p => p[0]).ToArray());
        Assert.Equal(new long[] { 3, 4 }, pages.Select(p => p[2]).ToArray());
    }

    /// <summary>Three boxes in one group all print the same serial (stub + ticket + sub-stub).</summary>
    [Fact]
    public void ThreeLinkedBoxes_AllShareTheSameSerial()
    {
        var job = Job(new[] { 1, 1, 1 }, start: 100, total: 3, NumberingMode.Linear);
        var pages = Grouped(job).GeneratePageNumbers(job).ToList();

        Assert.Equal(new long[] { 100, 100, 100 }, pages[0]);
        Assert.Equal(new long[] { 101, 101, 101 }, pages[1]);
    }

    /// <summary>The effective numbers-per-sheet is the UNIT count, not the raw box count.</summary>
    [Fact]
    public void UnitCount_CountsGroupsNotBoxes()
    {
        // Three booklets of two linked boxes each = 3 units.
        Assert.Equal(3, Grouped(Job(new[] { 1, 1, 2, 2, 3, 3 }, 1, 6, NumberingMode.Imposed)).UnitCount);
        // One group of two boxes + three ungrouped boxes = 4 units.
        Assert.Equal(4, Grouped(Job(new[] { 1, 1, 0, 0, 0 }, 1, 6, NumberingMode.Imposed)).UnitCount);
    }

    /// <summary>With no groups, the wrapper is a transparent pass-through of the inner strategy.</summary>
    [Fact]
    public void NoGroups_IsIdenticalToTheUnderlyingStrategy()
    {
        var job = Job(new[] { 0, 0, 0, 0 }, start: 1, total: 8, NumberingMode.Imposed);
        var wrapped = Grouped(job).GeneratePageNumbers(job).ToList();
        var bare = new CuttingStrategy().GeneratePageNumbers(job).ToList();

        Assert.Equal(bare.Count, wrapped.Count);
        for (int i = 0; i < bare.Count; i++)
            Assert.Equal(bare[i], wrapped[i]);
    }
}
