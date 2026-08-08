using System.Collections.Generic;
using System.Linq;
using Apex.NumberedBooksEngine.Core;
using Apex.NumberedBooksEngine.Models;

namespace Apex.Services.Tests.Numbering;

/// <summary>
/// More than one independent counter on the same sheet.
///
/// A receipt book runs its receipt number and a separate audit control number side by
/// side — different ranges, different prefixes, neither derived from the other. With a
/// single counter the second number could only be produced by running the whole job
/// twice and collating by hand, and any mistake in that collation is only discovered
/// once the books are bound.
///
/// The rule that must not break: slots of the SAME series stay consecutive (that is
/// what makes a cut stack come out in order), while different series advance alone.
/// </summary>
public class MultipleSeriesTests
{
    private static SlotSpec Slot(string id, string? series = null) =>
        new(id, 0, 0, 0.2f, 0.05f, "Arial", 12, "#000", TextAlign.Left, 0, null,
            SlotKind.Text, null, series);

    private static readonly NumberSequencer Sequencer = new();

    // ── Independence ─────────────────────────────────────────────────────────

    [Fact]
    public void TwoSeriesOnOneSheet_AdvanceIndependently()
    {
        var slots = new List<SlotSpec> { Slot("receipt", "REC"), Slot("control", "CTL") };
        var series = new List<NumberSeries>
        {
            new("REC", StartNumber: 1000, TotalNumbers: 3),
            new("CTL", StartNumber: 7, TotalNumbers: 3),
        };

        var pages = Sequencer.GenerateLinearAssignments(slots, series).ToList();

        Assert.Equal(3, pages.Count);
        Assert.Equal(new long[] { 1000, 1001, 1002 },
            pages.Select(p => p.SlotNumbers.Single(a => a.SlotId == "receipt").Number));
        Assert.Equal(new long[] { 7, 8, 9 },
            pages.Select(p => p.SlotNumbers.Single(a => a.SlotId == "control").Number));
    }

    [Fact]
    public void EachSeriesKeepsItsOwnStep()
    {
        var slots = new List<SlotSpec> { Slot("a", "A"), Slot("b", "B") };
        var series = new List<NumberSeries>
        {
            new("A", StartNumber: 1, TotalNumbers: 3, Step: 1),
            new("B", StartNumber: 100, TotalNumbers: 3, Step: 50),
        };

        var pages = Sequencer.GenerateLinearAssignments(slots, series).ToList();

        Assert.Equal(new long[] { 1, 2, 3 },
            pages.Select(p => p.SlotNumbers.Single(a => a.SlotId == "a").Number));
        Assert.Equal(new long[] { 100, 150, 200 },
            pages.Select(p => p.SlotNumbers.Single(a => a.SlotId == "b").Number));
    }

    [Fact]
    public void EachSeriesFormatsWithItsOwnPrefixAndPadding()
    {
        // The reason SeriesId travels with the assignment: identical digits must be
        // able to print as different documents.
        var rec = new NumberSeries("REC", 123, 1,
            Format: new NumberFormatOptions(PadDigits: 6, Prefix: "REC-"));
        var ctl = new NumberSeries("CTL", 123, 1,
            Format: new NumberFormatOptions(PadDigits: 4, Prefix: "2026/"));

        Assert.Equal("REC-000123", rec.FormatValue(123));
        Assert.Equal("2026/0123", ctl.FormatValue(123));
    }

    // ── Cut-stack ordering ───────────────────────────────────────────────────

    [Fact]
    public void SlotsOfTheSameSeries_StayConsecutiveOnASheet()
    {
        // Linear/Shershara: a sheet is filled before moving to the next.
        var slots = new List<SlotSpec> { Slot("s1", "A"), Slot("s2", "A"), Slot("s3", "A") };
        var series = new List<NumberSeries> { new("A", 1, 6) };

        var pages = Sequencer.GenerateLinearAssignments(slots, series).ToList();

        Assert.Equal(2, pages.Count);
        Assert.Equal(new long[] { 1, 2, 3 }, pages[0].SlotNumbers.Select(a => a.Number));
        Assert.Equal(new long[] { 4, 5, 6 }, pages[1].SlotNumbers.Select(a => a.Number));
    }

    [Fact]
    public void ImposedMode_BuildsOnePilePerSlotPosition()
    {
        // Cutting: slot 1 of every sheet becomes one pile that reads 1,2,3 top to
        // bottom. Getting this backwards produces books numbered 1,4,7 after cutting.
        var slots = new List<SlotSpec> { Slot("s1", "A"), Slot("s2", "A") };
        var series = new List<NumberSeries> { new("A", 1, 6) };

        var pages = Sequencer.GenerateImposedAssignments(slots, series).ToList();

        Assert.Equal(3, pages.Count);
        Assert.Equal(new long[] { 1, 2, 3 },
            pages.Select(p => p.SlotNumbers.Single(a => a.SlotId == "s1").Number));
        Assert.Equal(new long[] { 4, 5, 6 },
            pages.Select(p => p.SlotNumbers.Single(a => a.SlotId == "s2").Number));
    }

    [Fact]
    public void ImposedMode_KeepsPilesSeparatePerSeries()
    {
        var slots = new List<SlotSpec>
        {
            Slot("a1", "A"), Slot("a2", "A"), Slot("b1", "B"),
        };
        var series = new List<NumberSeries>
        {
            new("A", 1, 4),
            new("B", 500, 2),
        };

        var pages = Sequencer.GenerateImposedAssignments(slots, series).ToList();

        Assert.Equal(2, pages.Count);
        Assert.Equal(new long[] { 1, 2 }, pages.Select(p => p.SlotNumbers.Single(a => a.SlotId == "a1").Number));
        Assert.Equal(new long[] { 3, 4 }, pages.Select(p => p.SlotNumbers.Single(a => a.SlotId == "a2").Number));
        Assert.Equal(new long[] { 500, 501 }, pages.Select(p => p.SlotNumbers.Single(a => a.SlotId == "b1").Number));
    }

    // ── Uneven series ────────────────────────────────────────────────────────

    [Fact]
    public void AShorterSeriesStops_WhileTheLongerOneKeepsPrinting()
    {
        var slots = new List<SlotSpec> { Slot("long", "L"), Slot("short", "S") };
        var series = new List<NumberSeries>
        {
            new("L", 1, 4),
            new("S", 900, 2),
        };

        var pages = Sequencer.GenerateLinearAssignments(slots, series).ToList();

        Assert.Equal(4, pages.Count);
        Assert.Equal(4, pages.Count(p => p.SlotNumbers.Any(a => a.SlotId == "long")));
        Assert.Equal(2, pages.Count(p => p.SlotNumbers.Any(a => a.SlotId == "short")));
    }

    [Fact]
    public void NoNumberIsIssuedTwiceWithinASeries()
    {
        // The one guarantee a numbered-document shop cannot compromise on.
        var slots = new List<SlotSpec>
        {
            Slot("a1", "A"), Slot("a2", "A"), Slot("a3", "A"), Slot("b1", "B"),
        };
        var series = new List<NumberSeries> { new("A", 1, 25), new("B", 1, 25) };

        foreach (var mode in new[] { true, false })
        {
            var pages = mode
                ? Sequencer.GenerateImposedAssignments(slots, series).ToList()
                : Sequencer.GenerateLinearAssignments(slots, series).ToList();

            foreach (var s in series)
            {
                var issued = pages
                    .SelectMany(p => p.SlotNumbers)
                    .Where(a => a.SeriesId == s.Id)
                    .Select(a => a.Number)
                    .ToList();

                Assert.Equal(issued.Count, issued.Distinct().Count());
                Assert.Equal(s.TotalNumbers, issued.Count);
            }
        }
    }

    // ── Backwards compatibility and safety ───────────────────────────────────

    [Fact]
    public void SlotsWithNoSeriesUseThePrimaryOne()
    {
        // Every template built before multi-series support must behave exactly as before.
        var slots = new List<SlotSpec> { Slot("s1"), Slot("s2") };
        var series = new List<NumberSeries> { new("MAIN", 1, 4) };

        var pages = Sequencer.GenerateLinearAssignments(slots, series).ToList();

        Assert.Equal(2, pages.Count);
        Assert.Equal(new long[] { 1, 2 }, pages[0].SlotNumbers.Select(a => a.Number));
        Assert.Equal(new long[] { 3, 4 }, pages[1].SlotNumbers.Select(a => a.Number));
    }

    [Fact]
    public void ASlotNamingAnUndefinedSeriesIsReported_NotSilentlyBlank()
    {
        var slots = new List<SlotSpec> { Slot("good", "A"), Slot("typo", "AA") };
        var series = new List<NumberSeries> { new("A", 1, 5) };

        var orphans = NumberSequencer.FindSlotsWithUnknownSeries(slots, series);

        Assert.Single(orphans);
        Assert.Equal("typo", orphans[0].Id);
    }

    [Fact]
    public void ASeriesWithNoSlotsDoesNotStallTheJob()
    {
        var slots = new List<SlotSpec> { Slot("a1", "A") };
        var series = new List<NumberSeries> { new("A", 1, 2), new("UNUSED", 1, 1000) };

        var pages = Sequencer.GenerateLinearAssignments(slots, series).ToList();

        Assert.Equal(2, pages.Count);
    }
}
