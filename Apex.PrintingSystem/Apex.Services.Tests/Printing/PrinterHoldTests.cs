using Apex.Core.Interfaces;
using Apex.Core.Models;
using Apex.Services;
using Apex.Services.Printing;

namespace Apex.Services.Tests.Printing;

/// <summary>
/// The shop's rule: a station that needs a person holds its copies until somebody
/// attends to it. Never skipped, never sent to a different printer — and the copies
/// left waiting are not reported as failures, because nothing went wrong with them.
/// </summary>
public class PrinterHoldTests
{
    /// <summary>A probe whose answers a test can change mid-run.</summary>
    private sealed class FakeHealth : IPrinterHealthProbe
    {
        private readonly Dictionary<string, PrinterCondition> _conditions =
            new(StringComparer.OrdinalIgnoreCase);

        public void Set(string printer, PrinterCondition condition) =>
            _conditions[printer] = condition;

        public PrinterStatusEventArgs? GetCurrentStatus(string printerName)
        {
            var c = _conditions.TryGetValue(printerName, out var v) ? v : PrinterCondition.Ready;
            return new PrinterStatusEventArgs(
                printerName, c, c.ToString(),
                isOffline: c == PrinterCondition.Offline,
                hasError: false, queueLength: 0);
        }
    }

    /// <summary>
    /// Unused by the batch — everything prints through SmartPrintManager — but the
    /// constructor requires one.
    ///
    /// That is why these tests assert on whether a copy was ATTEMPTED rather than
    /// whether paper came out: the target names below are not real devices, so every
    /// attempt fails at the spooler. What is under test is the dispatch decision —
    /// which stations were fed and which were held back — and that is decided before
    /// any device is touched.
    /// </summary>
    private sealed class UnusedEngine : IPrintEngine
    {
        public Task<bool> PrintAsync(string printerName, string filePath) => Task.FromResult(false);
        public Task<bool> PrintTestPageAsync(string printerName) => Task.FromResult(false);
    }

    private sealed class NullLogger : IPrintJobLogger
    {
        public void LogJob(string printerName, string filePath, bool success, string message) { }
    }

    private static (BatchPrintJobManager mgr, FakeHealth health) NewManager()
    {
        var health = new FakeHealth();
        return (new BatchPrintJobManager(new UnusedEngine(), new NullLogger(), null, health), health);
    }

    /// <summary>No retries — the targets are not real devices and each retry costs seconds.</summary>
    private static BatchSettings Fast() => new() { RetryCount = 0 };

    private static List<BatchJob> OneFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"apex_hold_{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, Imposition.PdfOperationsServiceTests.MakePdf(1));
        // OriginalExtension is derived from FilePath, so a .pdf path is what makes
        // the batch skip conversion.
        return new List<BatchJob> { new() { FilePath = path } };
    }

    /// <summary>
    /// A tray that is merely low still prints. Treating "Low Paper" as a fault would
    /// stop a station that could have finished the run — which is what the old status
    /// mapping did, by lumping it in with an empty tray.
    /// </summary>
    [Theory]
    [InlineData(PrinterCondition.LowPaper)]
    [InlineData(PrinterCondition.LowToner)]
    public async Task AWarningDoesNotHoldTheRun(PrinterCondition warning)
    {
        var (mgr, health) = NewManager();
        health.Set("P1", warning);

        var jobs = OneFile();
        try
        {
            var result = await mgr.ProcessBatchAsync(
                new[] { "P1" }, jobs, Fast(), PrintDistributionMode.Duplicate);

            Assert.Equal(0, result.CopiesHeld);
        }
        finally { File.Delete(jobs[0].FilePath); }
    }

    /// <summary>
    /// The point of holding per printer: one open cover must not idle the stations
    /// behind it. The healthy printers take their copies while the faulty one waits.
    /// </summary>
    [Fact]
    public async Task AFaultyPrinterDoesNotStopTheHealthyOnes()
    {
        var (mgr, health) = NewManager();
        health.Set("P2", PrinterCondition.DoorOpen);

        var jobs = OneFile();
        try
        {
            // P2 will never recover, so the operator gives up on it — otherwise the
            // run would correctly wait forever.
            mgr.OnPrinterHeld += (_, e) => mgr.AbandonPrinter(e.PrinterName);

            var result = await mgr.ProcessBatchAsync(
                new[] { "P1", "P2", "P3" }, jobs, Fast(), PrintDistributionMode.Duplicate);

            // P1 and P3 were fed; P2 was held back. Whether the two attempts reached
            // paper is a question for a real device — the claim here is that the open
            // cover on P2 did not stop them being tried.
            Assert.Equal(2, result.CopiesPrinted + result.CopiesFailed);
            Assert.Equal(1, result.CopiesHeld);
        }
        finally { File.Delete(jobs[0].FilePath); }
    }

    /// <summary>
    /// Held is a third outcome. Counting a waiting copy as a failure sends the
    /// operator looking for a fault when the answer is to close a cover.
    /// </summary>
    [Fact]
    public async Task AHeldCopyIsNotReportedAsAFailure()
    {
        var (mgr, health) = NewManager();
        health.Set("P1", PrinterCondition.OutOfPaper);

        var jobs = OneFile();
        try
        {
            mgr.OnPrinterHeld += (_, e) => mgr.AbandonPrinter(e.PrinterName);

            var result = await mgr.ProcessBatchAsync(
                new[] { "P1" }, jobs, Fast(), PrintDistributionMode.Duplicate);

            Assert.True(result.HasHeldCopies);
            Assert.Empty(result.Failures);
            Assert.False(result.AllSucceeded);

            var held = Assert.Single(result.HeldCopies);
            Assert.Equal("P1", held.Printer);
            Assert.Equal("OutOfPaper", held.Reason);
        }
        finally { File.Delete(jobs[0].FilePath); }
    }

    /// <summary>
    /// The alert has to name the station and what to fix — "a printer needs you" is
    /// not something an operator can act on without walking the floor.
    /// </summary>
    [Fact]
    public async Task TheOperatorIsToldWhichPrinterAndWhy()
    {
        var (mgr, health) = NewManager();
        health.Set("Ricoh C7200", PrinterCondition.PaperJam);

        var announced = new List<PrinterHoldEventArgs>();
        mgr.OnPrinterHeld += (_, e) => { announced.Add(e); mgr.AbandonPrinter(e.PrinterName); };

        var jobs = OneFile();
        try
        {
            await mgr.ProcessBatchAsync(
                new[] { "Ricoh C7200" }, jobs, Fast(), PrintDistributionMode.Duplicate);

            Assert.NotEmpty(announced);
            Assert.Equal("Ricoh C7200", announced[0].PrinterName);
            Assert.Equal("PaperJam", announced[0].Fault);
        }
        finally { File.Delete(jobs[0].FilePath); }
    }

    /// <summary>
    /// The whole point of the rewrite: every selected printer runs at once, not one
    /// file fully printed before the next begins. Proven without touching a device:
    /// hold ALL of them at the same time, and only release the trays once every station
    /// has announced its hold. A sequential batch would hold P1, wait for it forever
    /// (the others never reached, so the trays are never filled) and never finish — so
    /// completing at all is proof the stations were held, and therefore fed, together.
    /// </summary>
    [Fact]
    public async Task EveryPrinterRunsAtOnce_NotOneAtATime()
    {
        var (mgr, health) = NewManager();
        var printers = new[] { "P1", "P2", "P3", "P4" };
        foreach (var p in printers) health.Set(p, PrinterCondition.OutOfPaper);

        var heldTogether = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>();
        var allHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        mgr.OnPrinterHeld += (_, e) =>
        {
            heldTogether[e.PrinterName] = true;
            if (heldTogether.Count == printers.Length) allHeld.TrySetResult();
        };
        // Fill every tray only after all four have been held at the same moment.
        _ = allHeld.Task.ContinueWith(_ =>
        {
            foreach (var p in printers) health.Set(p, PrinterCondition.Ready);
        });

        var jobs = OneFile();
        try
        {
            var run = mgr.ProcessBatchAsync(printers, jobs, Fast(), PrintDistributionMode.Duplicate);
            var finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(15)));
            Assert.True(finished == run,
                "The batch did not finish — printers were held one at a time, not in parallel.");

            await run;
            Assert.Equal(printers.Length, heldTogether.Count);
            Assert.Equal(0, (await run).CopiesHeld); // all released, none abandoned
        }
        finally { File.Delete(jobs[0].FilePath); }
    }

    /// <summary>
    /// Once the tray is filled the copy is sent — the whole reason for holding rather
    /// than skipping. It must not stay held after the fault clears.
    /// </summary>
    [Fact]
    public async Task TheCopyIsSentOnceTheFaultIsCleared()
    {
        var (mgr, health) = NewManager();
        health.Set("P1", PrinterCondition.OutOfPaper);

        // Somebody fills the tray as soon as the alert appears.
        mgr.OnPrinterHeld += (_, e) => health.Set(e.PrinterName, PrinterCondition.Ready);

        var jobs = OneFile();
        try
        {
            var result = await mgr.ProcessBatchAsync(
                new[] { "P1" }, jobs, Fast(), PrintDistributionMode.Duplicate);

            // Released, not abandoned: the copy left the hold and was sent.
            Assert.Equal(0, result.CopiesHeld);
            Assert.Equal(1, result.CopiesPrinted + result.CopiesFailed);
        }
        finally { File.Delete(jobs[0].FilePath); }
    }
}
