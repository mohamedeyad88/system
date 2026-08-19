using Apex.UI.ViewModels;

namespace Apex.UI.Tests;

/// <summary>
/// The log viewer read only the application log, so after a successful print run it
/// showed one line about the monitoring service and nothing about the job — the file
/// built for diagnosing print failures was invisible in the one place an operator
/// would look.
/// </summary>
public class LogEntryMergeTests
{
    private const string AppLog =
        "[07:36:54] [Info] [5dc9ebed] Printer Monitoring Service starting.\n" +
        "[12:05:00] [Warning] [5dc9ebed] Something later.\n";

    private const string PrintLog =
        "2026-08-16 07:40:10.100 [INF] === Apex Print OS 2.6.0.0 started ===\n" +
        "2026-08-16 12:03:28.490 [WRN] ========== NEW PRINT JOB STARTED ==========\n" +
        "JobId: \"abc\"\n" +
        "File: F:\\book.pdf\n" +
        "2026-08-16 12:04:00.000 [ERR] Page 2 failed.\n";

    [Fact]
    public void BothLogsAppearInOneStream()
    {
        var merged = LogEntryMerge.Merge(AppLog, PrintLog);
        var text = LogEntryMerge.Render(merged);

        Assert.Contains("Printer Monitoring Service starting", text);
        Assert.Contains("NEW PRINT JOB STARTED", text);
        Assert.Contains("Page 2 failed", text);
    }

    [Fact]
    public void EntriesComeOutInTimeOrder()
    {
        var merged = LogEntryMerge.Merge(AppLog, PrintLog);
        var times = merged.Select(e => e.Time).ToList();

        Assert.Equal(times.OrderBy(t => t), times);

        // 07:36 app line precedes the 07:40 print line, which precedes 12:03.
        var text = LogEntryMerge.Render(merged);
        Assert.True(text.IndexOf("Monitoring Service") < text.IndexOf("2.6.0.0 started"));
        Assert.True(text.IndexOf("2.6.0.0 started") < text.IndexOf("NEW PRINT JOB"));
    }

    /// <summary>
    /// The print log writes a header then indented detail lines. Those carry no
    /// timestamp, so a naive sort would scatter them away from their job.
    /// </summary>
    [Fact]
    public void DetailLinesStayWithTheirJobHeader()
    {
        var merged = LogEntryMerge.Merge(AppLog, PrintLog);
        var text = LogEntryMerge.Render(merged);

        int header = text.IndexOf("NEW PRINT JOB STARTED");
        int jobId = text.IndexOf("JobId:");
        int file = text.IndexOf("File: F:");
        int later = text.IndexOf("Page 2 failed");

        Assert.True(header < jobId && jobId < file, "detail lines drifted from their header");
        Assert.True(file < later, "detail lines sorted past a later entry");
    }

    /// <summary>
    /// The two files spell severities differently — [Warning] vs [WRN]. Filtering on
    /// one spelling silently hid the other file.
    /// </summary>
    [Theory]
    [InlineData(LogLevelFilter.Warning, "NEW PRINT JOB STARTED")]
    [InlineData(LogLevelFilter.Warning, "Something later")]
    [InlineData(LogLevelFilter.Error, "Page 2 failed")]
    [InlineData(LogLevelFilter.Info, "Monitoring Service")]
    public void LevelFilterUnderstandsBothVocabularies(LogLevelFilter level, string expected)
    {
        var kept = LogEntryMerge.Merge(AppLog, PrintLog)
                                .Where(e => LogEntryMerge.MatchesLevel(e, level));

        Assert.Contains(expected, LogEntryMerge.Render(kept));
    }

    [Fact]
    public void EachLineSaysWhichLogItCameFrom()
    {
        var text = LogEntryMerge.Render(LogEntryMerge.Merge(AppLog, PrintLog));

        Assert.Contains("[APP  ] [07:36:54]", text);
        Assert.Contains("[PRINT] 2026-08-16 12:04:00.000", text);
    }

    [Fact]
    public void AMissingPrintLogIsNotAnError()
    {
        var merged = LogEntryMerge.Merge(AppLog, "");
        Assert.Contains("Monitoring Service", LogEntryMerge.Render(merged));
    }
}
