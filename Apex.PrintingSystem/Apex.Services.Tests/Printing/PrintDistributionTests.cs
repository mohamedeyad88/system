using Apex.Services.Printing;

namespace Apex.Services.Tests.Printing;

/// <summary>
/// How a batch of files is spread over several printers.
///
/// The two modes answer different jobs and must never be confused:
/// LoadBalance prints each file ONCE (finish a queue faster), Duplicate prints each
/// file on EVERY printer (same documents at each station). Getting this backwards
/// either loses copies a branch was expecting, or prints the whole queue N times.
/// </summary>
public class PrintDistributionTests
{
    private static readonly string[] ThreePrinters = { "HP-01", "Ricoh-2", "Canon-3" };

    // ── Load balance ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, "HP-01")]
    [InlineData(1, "Ricoh-2")]
    [InlineData(2, "Canon-3")]
    [InlineData(3, "HP-01")]      // wraps around
    [InlineData(4, "Ricoh-2")]
    public void LoadBalance_DealsFilesRoundRobin(int jobIndex, string expected)
    {
        var targets = BatchPrintJobManager.TargetsFor(
            ThreePrinters, PrintDistributionMode.LoadBalance, jobIndex);

        Assert.Equal(new[] { expected }, targets);
    }

    [Fact]
    public void LoadBalance_SendsEachFileToExactlyOnePrinter()
    {
        for (int i = 0; i < 10; i++)
        {
            var targets = BatchPrintJobManager.TargetsFor(
                ThreePrinters, PrintDistributionMode.LoadBalance, i);
            Assert.Single(targets);
        }
    }

    [Fact]
    public void LoadBalance_SpreadsEvenlyAcrossAWholeQueue()
    {
        var counts = new Dictionary<string, int>();
        for (int i = 0; i < 9; i++)
        {
            var target = BatchPrintJobManager.TargetsFor(
                ThreePrinters, PrintDistributionMode.LoadBalance, i)[0];
            counts[target] = counts.GetValueOrDefault(target) + 1;
        }

        Assert.All(counts.Values, c => Assert.Equal(3, c));
    }

    // ── Duplicate ─────────────────────────────────────────────────────────────

    [Fact]
    public void Duplicate_SendsEveryFileToEveryPrinter()
    {
        var targets = BatchPrintJobManager.TargetsFor(
            ThreePrinters, PrintDistributionMode.Duplicate, jobIndex: 0);

        Assert.Equal(ThreePrinters, targets);
    }

    [Fact]
    public void Duplicate_IsIndependentOfTheJobIndex()
    {
        var first = BatchPrintJobManager.TargetsFor(ThreePrinters, PrintDistributionMode.Duplicate, 0);
        var later = BatchPrintJobManager.TargetsFor(ThreePrinters, PrintDistributionMode.Duplicate, 7);

        Assert.Equal(first, later);
    }

    // ── Degenerate input ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(PrintDistributionMode.LoadBalance)]
    [InlineData(PrintDistributionMode.Duplicate)]
    public void SinglePrinter_BehavesTheSameInBothModes(PrintDistributionMode mode)
    {
        var one = new[] { "HP-01" };

        for (int i = 0; i < 3; i++)
            Assert.Equal(one, BatchPrintJobManager.TargetsFor(one, mode, i));
    }

    [Theory]
    [InlineData(PrintDistributionMode.LoadBalance)]
    [InlineData(PrintDistributionMode.Duplicate)]
    public void NoPrinters_ProducesNoTargetsInsteadOfThrowing(PrintDistributionMode mode)
    {
        Assert.Empty(BatchPrintJobManager.TargetsFor(Array.Empty<string>(), mode, 0));
    }

    [Fact]
    public void NegativeIndex_DoesNotThrowOrPickOutOfRange()
    {
        var targets = BatchPrintJobManager.TargetsFor(
            ThreePrinters, PrintDistributionMode.LoadBalance, jobIndex: -1);

        Assert.Equal(new[] { "HP-01" }, targets);
    }

    // ── Guard rails on the public entry point ─────────────────────────────────

    [Fact]
    public async Task EmptyPrinterList_IsRejected()
    {
        var mgr = NewManager();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            mgr.ProcessBatchAsync(Array.Empty<string>(), new(), new Apex.Core.Models.BatchSettings()));
    }

    [Fact]
    public async Task PrinterListOfBlanks_IsRejected()
    {
        var mgr = NewManager();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            mgr.ProcessBatchAsync(new[] { "", "   " }, new(), new Apex.Core.Models.BatchSettings()));
    }

    /// <summary>
    /// The manager only needs to exist for the argument guards; no printing happens
    /// because the guards reject before any job is touched.
    /// </summary>
    private static BatchPrintJobManager NewManager() =>
        (BatchPrintJobManager)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(BatchPrintJobManager));
}
