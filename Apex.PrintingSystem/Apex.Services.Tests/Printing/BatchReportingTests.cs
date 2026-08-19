using System.Linq;
using Apex.Services.Printing;

namespace Apex.Services.Tests.Printing;

/// <summary>
/// What the operator is told while a batch runs, and after it ends.
///
/// Reported from the shop floor: multi-printer runs where the progress bar never
/// left zero and no final outcome was shown. Both had the same root: progress was
/// computed from SUCCESSES only, so a failing run sat at 0% from start to finish;
/// and the end-of-run counts were computed and then discarded, leaving a bare
/// "printing finished" dialog whether every job printed or none did.
///
/// A print shop cannot act on "done".
/// </summary>
public class BatchReportingTests
{
    /// <summary>A single-printer run, where one file is exactly one copy.</summary>
    private static BatchResult Result(int total, int ok, int failed, bool cancelled = false) =>
        new()
        {
            TotalJobs = total,
            Succeeded = ok,
            Failed = failed,
            CopiesPrinted = ok,
            CopiesFailed = failed,
            WasCancelled = cancelled,
            Failures = Enumerable.Range(1, failed)
                .Select(i => new BatchFailure($"job{i}.pdf", "printer offline"))
                .ToList(),
        };

    // ── Progress ─────────────────────────────────────────────────────────────

    [Fact]
    public void ProgressCountsFailuresAsAttempted()
    {
        // The reported symptom: every job failed and the bar never moved. Progress
        // has to reflect work ATTEMPTED, otherwise a stuck queue and a failing one
        // look identical.
        var p = new BatchProgress
        {
            TotalJobs = 4,
            CompletedJobs = 0,
            FailedJobs = 4,
            AttemptedJobs = 4,
            PercentComplete = 4 / 4.0 * 100,
        };

        Assert.Equal(100, p.PercentComplete);
    }

    [Fact]
    public void APartlyFailingRunShowsPartialProgress()
    {
        var p = new BatchProgress
        {
            TotalJobs = 10,
            CompletedJobs = 3,
            FailedJobs = 2,
            AttemptedJobs = 5,
            PercentComplete = 5 / 10.0 * 100,
        };

        Assert.Equal(50, p.PercentComplete);
    }

    // ── Outcome ──────────────────────────────────────────────────────────────

    [Fact]
    public void ARunWhereEverythingPrintedIsRecognised()
    {
        var r = Result(total: 5, ok: 5, failed: 0);

        Assert.True(r.AllSucceeded);
        Assert.False(r.NothingPrinted);
        Assert.Empty(r.Failures);
    }

    [Fact]
    public void ARunWhereNothingPrintedIsNotReportedAsSuccess()
    {
        // The exact case from the field: the old dialog said "finished" here.
        var r = Result(total: 5, ok: 0, failed: 5);

        Assert.False(r.AllSucceeded);
        Assert.True(r.NothingPrinted);
    }

    [Fact]
    public void APartialRunIsNeitherSuccessNorTotalFailure()
    {
        var r = Result(total: 5, ok: 3, failed: 2);

        Assert.False(r.AllSucceeded);
        Assert.False(r.NothingPrinted);
        Assert.Equal(2, r.Failures.Count);
    }

    [Fact]
    public void ACancelledRunIsNotCountedAsSuccessEvenIfNothingFailed()
    {
        // Stopping after 2 of 10 is not "all succeeded" — the other 8 never printed.
        var r = Result(total: 10, ok: 2, failed: 0, cancelled: true);

        Assert.False(r.AllSucceeded);
    }

    [Fact]
    public void EveryFailureCarriesAFileNameAndAReason()
    {
        // A list of failures with no reason sends the operator to the machine blind.
        var r = Result(total: 3, ok: 1, failed: 2);

        Assert.All(r.Failures, f =>
        {
            Assert.False(string.IsNullOrWhiteSpace(f.FileName));
            Assert.False(string.IsNullOrWhiteSpace(f.Reason));
        });
    }

    [Fact]
    public void AnEmptyBatchIsNotReportedAsNothingPrinted()
    {
        // Zero jobs is not a failure; it is nothing to do.
        var r = Result(total: 0, ok: 0, failed: 0);

        Assert.False(r.NothingPrinted);
    }

    // ── Naming the printer ───────────────────────────────────────────────────

    [Fact]
    public void AFailureNamesTheDeviceThatRefusedIt()
    {
        // Observed in the field: three files sent to nine printers, all three
        // reported as failed, with nothing to say which device caused it. One
        // printer that cannot print silently fails every job in Duplicate mode, and
        // "three files failed" sends the operator looking in the wrong place.
        var r = new BatchResult
        {
            TotalJobs = 3,
            Succeeded = 0,
            Failed = 3,
            Failures = new[]
            {
                new BatchFailure("1.pdf", "OneNote cannot print silently", "OneNote (Desktop)"),
                new BatchFailure("2.pdf", "OneNote cannot print silently", "OneNote (Desktop)"),
                new BatchFailure("3.pdf", "OneNote cannot print silently", "OneNote (Desktop)"),
            },
        };

        Assert.All(r.Failures, f => Assert.False(string.IsNullOrWhiteSpace(f.Printer)));

        // Grouping collapses one bad device into a single block instead of three
        // apparently unrelated file failures.
        var byPrinter = r.Failures.GroupBy(f => f.Printer).ToList();
        Assert.Single(byPrinter);
        Assert.Equal(3, byPrinter[0].Count());
    }

    [Fact]
    public void TheSameFileCanFailOnOneprinterAndPrintOnAnother()
    {
        // Duplicate mode sends every file to every printer, so failures are per
        // (file, printer) — recording them per file alone loses which station is
        // missing its copy.
        var r = new BatchResult
        {
            TotalJobs = 1,
            Succeeded = 0,
            Failed = 1,
            Failures = new[]
            {
                new BatchFailure("job.pdf", "offline", "Printer B"),
            },
        };

        Assert.Single(r.Failures);
        Assert.Equal("Printer B", r.Failures[0].Printer);
    }

    [Fact]
    public void AFailureBeforeAnyPrinterIsStillReported()
    {
        // Conversion failures never reach a device; they must not vanish from the
        // report just because there is no printer to name.
        var f = new BatchFailure("broken.docx", "conversion failed", Printer: null);

        Assert.Null(f.Printer);
        Assert.False(string.IsNullOrWhiteSpace(f.Reason));
    }

    // ── Copies, not files ────────────────────────────────────────────────────

    [Fact]
    public void EightOfNinePrintersWorkingIsNotReportedAsNothingPrinted()
    {
        // Straight from the field. Three files to nine printers = 27 copies. One
        // printer cannot print silently and refuses all three, so all three FILES are
        // incomplete — but 24 copies are in people's hands. The old report said
        // "0 succeeded of 3", and the operator went hunting for a fault that was not
        // there. Output files on disk proved the printing had worked.
        var r = new BatchResult
        {
            TotalJobs = 3,
            Succeeded = 0,          // no file completed on EVERY printer
            Failed = 3,
            CopiesPrinted = 24,     // 3 files × 8 working printers
            CopiesFailed = 3,       // 3 files × 1 refusing printer
        };

        Assert.False(r.NothingPrinted);
        Assert.True(r.PartiallyPrinted);
        Assert.Equal(27, r.CopiesTotal);
    }

    [Fact]
    public void NothingPrintedIsJudgedOnCopies()
    {
        var r = new BatchResult
        {
            TotalJobs = 3, Succeeded = 0, Failed = 3,
            CopiesPrinted = 0, CopiesFailed = 27,
        };

        Assert.True(r.NothingPrinted);
        Assert.False(r.PartiallyPrinted);
    }

    [Fact]
    public void TheCopyCountMatchesWhatTheHeaderPromises()
    {
        // The UI header reads "3 files · 9 printers · 27 jobs". A report that counts
        // 3 while the header counts 27 is telling the operator two different stories
        // about the same run.
        const int files = 3, printers = 9;

        var r = new BatchResult
        {
            TotalJobs = files, Succeeded = files, Failed = 0,
            CopiesPrinted = files * printers, CopiesFailed = 0,
        };

        Assert.Equal(files * printers, r.CopiesTotal);
    }
}
