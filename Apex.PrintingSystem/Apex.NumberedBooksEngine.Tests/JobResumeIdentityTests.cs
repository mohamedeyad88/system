using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Apex.NumberedBooksEngine.Core;
using Apex.NumberedBooksEngine.Models;
using Xunit;

namespace Apex.NumberedBooksEngine.Tests;

/// <summary>
/// Finding an interrupted run again.
///
/// Checkpoints were written under <c>Guid.NewGuid()</c> and then looked up by that same
/// freshly-minted guid moments later, so the lookup could never match. Every checkpoint
/// the engine had ever written was unreachable, and a run that stopped at sheet 12,000 of
/// 20,000 had no way back — the operator counted the stack by hand and guessed.
///
/// What makes two runs "the same job" to a press is the design, the series and the range.
/// These lock that definition, because getting it wrong in either direction hurts: too
/// loose and an unrelated job offers to resume into the wrong numbers; too strict and the
/// resume never appears when it is needed.
/// </summary>
public class JobResumeIdentityTests
{
    private static NumberedPrintJobOptions Job(
        string template = @"C:\designs\receipt.png",
        long start = 1,
        long total = 500,
        int copies = 1,
        string printer = "EPSON WF-C5210 Series",
        string? prefix = null)
        => new(
            PrinterName: printer,
            TemplatePath: template,
            Dpi: 300,
            StartNumber: start,
            TotalNumbers: total,
            CopiesPerPage: copies,
            Slots: new List<SlotSpec>
            {
                new("s1", 0.1f, 0.1f, 0.3f, 0.08f, "Arial", 24f, "#000000", TextAlign.Center, 0f, null)
            },
            UsePrinterStoredTemplate: false,
            LowResourceMode: false,
            CheckpointEvery: 100,
            NumberFormat: prefix == null ? null : NumberFormatOptions.Default with { Prefix = prefix });

    [Fact]
    public void TheSameJobKeepsTheSameIdentityAcrossRuns()
    {
        Assert.Equal(JobOrchestrator.JobIdentity(Job()), JobOrchestrator.JobIdentity(Job()));
    }

    /// <summary>
    /// The original fault, stated as a test: a fresh identity every run means the
    /// checkpoint written by the previous attempt can never be found.
    /// </summary>
    [Fact]
    public void TheIdentityIsNotRandom()
    {
        var ids = new HashSet<string>();
        for (int i = 0; i < 5; i++) ids.Add(JobOrchestrator.JobIdentity(Job()));
        Assert.Single(ids);
    }

    [Theory]
    [InlineData(@"C:\designs\invoice.png", 1L, 500L, 1, null)]   // different design
    [InlineData(@"C:\designs\receipt.png", 501L, 500L, 1, null)] // different start
    [InlineData(@"C:\designs\receipt.png", 1L, 900L, 1, null)]   // different count
    [InlineData(@"C:\designs\receipt.png", 1L, 500L, 2, null)]   // different copies
    [InlineData(@"C:\designs\receipt.png", 1L, 500L, 1, "INV-")] // different series
    public void ADifferentJobGetsADifferentIdentity(
        string template, long start, long total, int copies, string? prefix)
    {
        Assert.NotEqual(
            JobOrchestrator.JobIdentity(Job()),
            JobOrchestrator.JobIdentity(Job(template, start, total, copies, prefix: prefix)));
    }

    /// <summary>
    /// Moving a stalled job to the machine next door is still the same job — a press does
    /// that routinely when a printer jams, and the resume has to survive it.
    /// </summary>
    [Fact]
    public void ChangingThePrinterIsStillTheSameJob()
    {
        Assert.Equal(
            JobOrchestrator.JobIdentity(Job()),
            JobOrchestrator.JobIdentity(Job(printer: "HP LaserJet M404")));
    }

    [Fact]
    public async Task ACheckpointWrittenByOneRunIsFoundByTheNext()
    {
        var dir = Path.Combine(Path.GetTempPath(), "apex-resume-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var manager = new CheckpointManager(dir, checkpointInterval: 500);
            var id = JobOrchestrator.JobIdentity(Job());

            // an attempt that died at 12,000
            await manager.SaveCheckpointAsync(id, lastNumber: 12000, pagesPrinted: 12000);

            // a later run of the same job asks what happened
            var found = await manager.LoadCheckpointAsync(JobOrchestrator.JobIdentity(Job()));

            Assert.NotNull(found);
            Assert.Equal(12000, found!.LastPrintedNumber);
            Assert.Equal(12001, await manager.GetResumeStartNumberAsync(id));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task AFinishedJobLeavesNothingToResume()
    {
        var dir = Path.Combine(Path.GetTempPath(), "apex-resume-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var manager = new CheckpointManager(dir);
            var id = JobOrchestrator.JobIdentity(Job());

            await manager.SaveCheckpointAsync(id, lastNumber: 500, pagesPrinted: 500);
            manager.DeleteCheckpoint(id);   // what the orchestrator does on success

            // Otherwise re-running an identical job would offer to resume at its own end.
            Assert.Null(await manager.LoadCheckpointAsync(id));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
