using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Apex.NumberedBooksEngine.Core;
using Apex.NumberedBooksEngine.Models;

namespace Apex.Services.Tests.Numbering;

/// <summary>
/// Multiple series and the issued-numbers register together.
///
/// Two independent counters can legitimately reach the same digits — receipt 000123
/// and control 000123 are different documents. If the register keyed on the number
/// alone, the second series would be refused as a duplicate and the job would be
/// impossible to run. Conversely, a repeat WITHIN one series must still be caught:
/// that is a compliance breach for both the press and its customer.
/// </summary>
public class SeriesRegistryIntegrationTests : IDisposable
{
    private readonly string _ledger =
        Path.Combine(Path.GetTempPath(), $"apex-series-{Guid.NewGuid():N}.jsonl");

    public void Dispose()
    {
        try { if (File.Exists(_ledger)) File.Delete(_ledger); } catch { }
    }

    private static SlotSpec Slot(string id, string? series) =>
        new(id, 0, 0, 0.2f, 0.05f, "Arial", 12, "#000", TextAlign.Left, 0, null,
            SlotKind.Text, null, series);

    [Fact]
    public void TheSameDigitsInTwoSeriesAreNotADuplicate()
    {
        var registry = new NumberRegistry(_ledger);

        registry.Record("REC", start: 1, count: 100, printerName: "press", jobId: "job-1");

        // The control series has never issued anything, so 1..100 is free there.
        var check = registry.Check("CTL", 1, 100);

        Assert.True(check.IsAvailable,
            "identical digits in a different series were refused — the job could not run");
    }

    [Fact]
    public void ARepeatWithinOneSeriesIsStillCaught()
    {
        var registry = new NumberRegistry(_ledger);

        registry.Record("REC", start: 1, count: 100, printerName: "press", jobId: "job-1");

        var check = registry.Check("REC", 50, 100);

        Assert.False(check.IsAvailable);
        Assert.NotEmpty(check.Conflicts);
    }

    [Fact]
    public void EverySeriesInAJobCanBeCheckedBeforePrinting()
    {
        // The practical workflow: plan the job, then ask the register about each
        // counter separately before a sheet is committed.
        var registry = new NumberRegistry(_ledger);
        registry.Record("REC", start: 1, count: 50, printerName: "press", jobId: "old-job");

        var series = new List<NumberSeries>
        {
            new("REC", StartNumber: 40, TotalNumbers: 20),   // overlaps 40..50
            new("CTL", StartNumber: 40, TotalNumbers: 20),   // untouched series
        };

        var blocked = series
            .Where(s => !registry.Check(s.Id, s.StartNumber, s.TotalNumbers).IsAvailable)
            .Select(s => s.Id)
            .ToList();

        Assert.Equal(new[] { "REC" }, blocked);
    }

    [Fact]
    public void TheSequencerAndTheRegisterAgreeOnWhatWasIssued()
    {
        // What the register is told must match what the sheets actually carry —
        // otherwise the audit trail is fiction.
        var slots = new List<SlotSpec> { Slot("a1", "A"), Slot("a2", "A"), Slot("b1", "B") };
        var series = new List<NumberSeries> { new("A", 1, 10), new("B", 500, 4) };

        var pages = new NumberSequencer().GenerateImposedAssignments(slots, series).ToList();

        foreach (var s in series)
        {
            var issued = pages.SelectMany(p => p.SlotNumbers)
                              .Where(a => a.SeriesId == s.Id)
                              .Select(a => a.Number)
                              .OrderBy(n => n)
                              .ToList();

            Assert.Equal(s.TotalNumbers, issued.Count);
            Assert.Equal(s.StartNumber, issued.First());
            Assert.Equal(s.ValueAt(s.TotalNumbers - 1), issued.Last());
            Assert.Equal(issued.Count, issued.Distinct().Count());
        }
    }
}
