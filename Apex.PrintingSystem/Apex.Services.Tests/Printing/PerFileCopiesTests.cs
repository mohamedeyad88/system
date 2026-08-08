using Apex.Core.Models;
using Apex.Services.Printing;

namespace Apex.Services.Tests.Printing;

/// <summary>
/// How many copies a queued file actually gets.
///
/// The queue lets the operator set copies per row while the toolbar sets a default
/// for the batch. If the batch default silently won, a row edited to 500 would print
/// 1 — a whole run reprinted by hand. And nothing may ever resolve to zero copies:
/// that is a job that looks submitted and produces no paper.
/// </summary>
public class PerFileCopiesTests
{
    private static BatchJob Job(int copies) => new() { FilePath = "a.pdf", Copies = copies };

    [Fact]
    public void PerFileCount_BeatsTheBatchDefault()
    {
        var copies = BatchPrintJobManager.CopiesFor(Job(500), new BatchSettings { Copies = 1 });

        Assert.Equal(500, copies);
    }

    [Fact]
    public void UnsetPerFileCount_FallsBackToTheBatchDefault()
    {
        var copies = BatchPrintJobManager.CopiesFor(Job(0), new BatchSettings { Copies = 3 });

        Assert.Equal(3, copies);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, 0)]
    [InlineData(0, -1)]
    public void NothingEverResolvesToZeroCopies(int jobCopies, int settingsCopies)
    {
        var copies = BatchPrintJobManager.CopiesFor(
            Job(jobCopies), new BatchSettings { Copies = settingsCopies });

        Assert.True(copies >= 1, $"resolved to {copies} copies — that prints nothing");
    }

    [Fact]
    public void ADefaultJobKeepsTheOldBatchBehaviour()
    {
        // Callers that only fill FilePath must be unaffected by the new field.
        var copies = BatchPrintJobManager.CopiesFor(
            new BatchJob { FilePath = "a.pdf" }, new BatchSettings { Copies = 7 });

        Assert.Equal(7, copies);
    }
}

/// <summary>
/// What the operator chose on the settings bar has to reach the driver.
///
/// The batch used to submit copies alone, so ticking Duplex or untucking Colour
/// changed nothing on paper — a whole run wasted before anyone noticed.
/// </summary>
public class BatchSettingsReachTheDriverTests
{
    private static readonly BatchJob PlainJob = new() { FilePath = "a.pdf" };

    [Fact]
    public void DuplexColourOrientationAndPaperAllTravelWithTheJob()
    {
        var resolved = BatchPrintJobManager.SettingsFor(PlainJob, new BatchSettings
        {
            Duplex = true,
            ColorMode = false,
            Orientation = "Landscape",
            PaperSize = "A3",
            Quality = "High",
        });

        Assert.True(resolved.Duplex);
        Assert.False(resolved.Color);
        Assert.Equal("Landscape", resolved.Orientation);
        Assert.Equal("A3", resolved.PaperSize);
        Assert.Equal("High", resolved.Quality);
    }

    [Fact]
    public void PerFilePageRange_BeatsTheBatchRange()
    {
        var job = new BatchJob { FilePath = "a.pdf", PageRange = "2-4" };

        var resolved = BatchPrintJobManager.SettingsFor(job, new BatchSettings { PageRange = "All" });

        Assert.Equal("2-4", resolved.PageRange);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyRangeMeansEverything_NotNothing(string batchRange)
    {
        var resolved = BatchPrintJobManager.SettingsFor(
            PlainJob, new BatchSettings { PageRange = batchRange });

        Assert.Equal("All", resolved.PageRange);
    }
}
