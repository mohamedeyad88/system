using System;
using System.IO;
using System.Linq;
using Apex.NumberedBooksEngine.Core;
using Xunit;

namespace Apex.NumberedBooksEngine.Tests;

/// <summary>
/// The number register — the control that stops the same invoice/receipt number
/// being printed twice.
///
/// This is a compliance control, not a convenience feature: a duplicated number on
/// a tax document is a breach for the press AND its customer, and a gap must be
/// explainable to an auditor. Overlap detection is therefore tested at every
/// boundary, because an off-by-one here silently authorises a duplicate.
/// </summary>
public class NumberRegistryTests : IDisposable
{
    private readonly string _file;
    private readonly NumberRegistry _reg;

    public NumberRegistryTests()
    {
        _file = Path.Combine(Path.GetTempPath(), $"apex-reg-{Guid.NewGuid():N}.jsonl");
        _reg = new NumberRegistry(_file);
    }

    public void Dispose()
    {
        try { if (File.Exists(_file)) File.Delete(_file); } catch { }
    }

    // ── Availability ──────────────────────────────────────────────────────────

    [Fact]
    public void EmptyRegister_EverythingIsAvailable()
    {
        Assert.True(_reg.Check("INV", 1, 100).IsAvailable);
    }

    [Fact]
    public void RecordedRange_IsNoLongerAvailable()
    {
        _reg.Record("INV", 1, 100, "HP-01");

        var result = _reg.Check("INV", 1, 100);

        Assert.Equal(RangeCheckStatus.Overlaps, result.Status);
        Assert.Single(result.Conflicts);
    }

    // ── Boundary conditions: where duplicates actually slip through ───────────

    [Theory]
    // recorded 100–199 ; (start, count, expectOverlap)
    [InlineData(1, 99, false)]    // ends exactly before
    [InlineData(1, 100, true)]    // touches the first number
    [InlineData(199, 1, true)]    // touches the last number
    [InlineData(200, 50, false)]  // starts exactly after
    [InlineData(150, 10, true)]   // fully inside
    [InlineData(50, 300, true)]   // fully contains
    [InlineData(99, 2, true)]     // straddles the start
    [InlineData(199, 5, true)]    // straddles the end
    public void OverlapIsDetectedAtEveryBoundary(long start, long count, bool expectOverlap)
    {
        _reg.Record("INV", 100, 100, "HP-01");   // 100–199

        var result = _reg.Check("INV", start, count);

        Assert.Equal(expectOverlap, !result.IsAvailable);
    }

    [Fact]
    public void AdjacentRanges_AreAllowed()
    {
        _reg.Record("INV", 1, 100, "HP-01");     // 1–100

        Assert.True(_reg.Check("INV", 101, 100).IsAvailable);
    }

    // ── Series isolation ──────────────────────────────────────────────────────

    [Fact]
    public void DifferentSeries_DoNotCollide()
    {
        _reg.Record("INV", 1, 100, "HP-01");

        Assert.True(_reg.Check("REC", 1, 100).IsAvailable);
    }

    [Theory]
    [InlineData("inv")]
    [InlineData("INV")]
    [InlineData("  Inv  ")]
    public void SeriesMatching_IgnoresCaseAndWhitespace(string variant)
    {
        _reg.Record("INV", 1, 100, "HP-01");

        // Otherwise "inv" would be treated as a new series and reissue the numbers.
        Assert.False(_reg.Check(variant, 1, 100).IsAvailable);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void BlankSeries_IsItsOwnValidSeries(string? series)
    {
        _reg.Record(series, 1, 50, "HP-01");

        Assert.False(_reg.Check(series, 25, 10).IsAvailable);
        Assert.True(_reg.Check("INV", 25, 10).IsAvailable);
    }

    // ── Invalid input ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void NonPositiveCount_IsRejected(long count)
    {
        Assert.Equal(RangeCheckStatus.Invalid, _reg.Check("INV", 1, count).Status);
    }

    [Fact]
    public void NegativeStart_IsRejected()
    {
        Assert.Equal(RangeCheckStatus.Invalid, _reg.Check("INV", -1, 10).Status);
    }

    [Fact]
    public void RangeThatWouldOverflow_IsRejectedNotWrapped()
    {
        // Wrapping would compute a negative end and silently "pass" the check.
        Assert.Equal(RangeCheckStatus.Invalid, _reg.Check("INV", long.MaxValue - 5, 100).Status);
    }

    [Fact]
    public void RecordingNonPositiveCount_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _reg.Record("INV", 1, 0, "HP-01"));
    }

    // ── Audit trail ───────────────────────────────────────────────────────────

    [Fact]
    public void RecordCapturesWhoWhatWhereWhen()
    {
        var rec = _reg.Record("INV", 500, 250, "Ricoh-2", jobId: "job-9", notes: "دفعة أولى");

        Assert.Equal("INV", rec.Series);
        Assert.Equal(500, rec.StartNumber);
        Assert.Equal(749, rec.EndNumber);
        Assert.Equal(250, rec.Count);
        Assert.Equal("Ricoh-2", rec.PrinterName);
        Assert.Equal("job-9", rec.JobId);
        Assert.Equal("دفعة أولى", rec.Notes);
        Assert.True(rec.PrintedUtc <= DateTime.UtcNow);
    }

    [Fact]
    public void LedgerIsAppendOnly_EarlierRecordsSurvive()
    {
        _reg.Record("INV", 1, 10, "A");
        _reg.Record("INV", 11, 10, "B");
        _reg.Record("INV", 21, 10, "C");

        Assert.Equal(3, _reg.GetSeries("INV").Count);
    }

    [Fact]
    public void LedgerSurvivesANewInstance()
    {
        _reg.Record("INV", 1, 100, "HP-01");

        var reopened = new NumberRegistry(_file);
        Assert.False(reopened.Check("INV", 50, 10).IsAvailable);
    }

    [Fact]
    public void CorruptLine_IsSkippedWithoutLosingValidRecords()
    {
        _reg.Record("INV", 1, 10, "A");
        File.AppendAllText(_file, "{ this is not json" + Environment.NewLine);
        _reg.Record("INV", 11, 10, "B");

        Assert.Equal(2, _reg.GetSeries("INV").Count);
    }

    // ── Gaps & next number ────────────────────────────────────────────────────

    [Fact]
    public void GapsBetweenRangesAreReported()
    {
        _reg.Record("INV", 1, 100, "A");     // 1–100
        _reg.Record("INV", 201, 100, "B");   // 201–300

        var gaps = _reg.FindGaps("INV");

        var gap = Assert.Single(gaps);
        Assert.Equal((101, 200), gap);
    }

    [Fact]
    public void ContiguousRanges_HaveNoGaps()
    {
        _reg.Record("INV", 1, 100, "A");
        _reg.Record("INV", 101, 100, "B");

        Assert.Empty(_reg.FindGaps("INV"));
    }

    [Fact]
    public void NestedRange_DoesNotCreateAPhantomGap()
    {
        _reg.Record("INV", 1, 1000, "A");    // 1–1000
        _reg.Record("INV", 200, 100, "B");   // inside the first

        Assert.Empty(_reg.FindGaps("INV"));
    }

    [Fact]
    public void NextAvailable_FollowsTheHighestIssuedNumber()
    {
        _reg.Record("INV", 1, 100, "A");
        _reg.Record("INV", 500, 100, "B");

        Assert.Equal(600, _reg.NextAvailable("INV"));
    }

    [Fact]
    public void NextAvailable_OnAnEmptySeries_StartsAtOne()
    {
        Assert.Equal(1, _reg.NextAvailable("NEW"));
    }

    [Fact]
    public void ConflictMessage_NamesTheDateAndPrinter()
    {
        _reg.Record("INV", 1, 100, "Ricoh-2");

        var msg = _reg.Check("INV", 50, 10).Message;

        Assert.Contains("Ricoh-2", msg);
        Assert.Contains(DateTime.UtcNow.ToLocalTime().ToString("yyyy"), msg);
    }
}
