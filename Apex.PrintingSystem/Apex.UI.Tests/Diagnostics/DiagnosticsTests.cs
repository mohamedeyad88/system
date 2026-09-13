using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Apex.UI.Diagnostics;
using Xunit;

namespace Apex.UI.Tests.Diagnostics;

public sealed class SessionGuardTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 9, 14, 8, 0, 0, DateTimeKind.Utc);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "apex-session-" + Guid.NewGuid().ToString("N"));
    private string Marker => Path.Combine(_dir, "session.running");

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void FirstStart_ReportsNothing_AndStartsWatching()
    {
        Assert.Null(SessionGuard.Begin(Marker, "2.8.3", T0, 1));
        Assert.True(File.Exists(Marker));
    }

    [Fact]
    public void CleanExit_ThenStart_ReportsNothing()
    {
        SessionGuard.Begin(Marker, "2.8.3", T0, 1);
        SessionGuard.End(Marker);
        Assert.Null(SessionGuard.Begin(Marker, "2.8.3", T0.AddHours(1), 2));
    }

    [Fact]
    public void StartWithoutExit_ReportsTheSessionThatNeverClosed()
    {
        SessionGuard.Begin(Marker, "2.8.2", T0, 1);

        var previous = SessionGuard.Begin(Marker, "2.8.3", T0.AddHours(3), 2);

        Assert.NotNull(previous);
        Assert.Equal(T0, previous!.StartedUtc);
        Assert.Equal("2.8.2", previous.Version);

        // The session that just started is now the one being watched.
        var next = SessionGuard.Begin(Marker, "2.8.3", T0.AddHours(4), 3);
        Assert.Equal(T0.AddHours(3), next!.StartedUtc);
    }

    [Fact]
    public void UnreadableMarker_StillCountsAsAnUncleanExit()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Marker, "not a date");

        var previous = SessionGuard.Begin(Marker, "2.8.3", T0, 1);

        Assert.NotNull(previous);
        Assert.Null(previous!.StartedUtc);
    }
}

public class WindowsCrashRecordsTests
{
    [Fact]
    public void RuntimeReport_NamesTheException()
    {
        const string text =
            "Application: Apex.UI.exe\r\nCoreCLR Version: 8.0.1024.46610\r\n" +
            "Description: The process was terminated due to an unhandled exception.\r\n" +
            "Exception Info: System.AccessViolationException: Attempted to read or write protected memory.\r\n" +
            "   at Apex.Something.Render()";

        Assert.True(WindowsCrashRecords.MentionsApex(text));
        Assert.StartsWith("System.AccessViolationException", WindowsCrashRecords.Summarize(text));
    }

    [Fact]
    public void FaultReport_ForTheInstalledExeName_NamesModuleAndCode()
    {
        const string text =
            "Faulting application name: ApexPrintOS.exe, version: 2.8.3.0, time stamp: 0x66000000\r\n" +
            "Faulting module name: dwrite.dll, version: 10.0.26100.1, time stamp: 0x00000000\r\n" +
            "Exception code: 0xc00000fd\r\nFault offset: 0x0000000000001234";

        Assert.True(WindowsCrashRecords.MentionsApex(text));
        Assert.Equal("module dwrite.dll · code 0xc00000fd", WindowsCrashRecords.Summarize(text));
    }

    [Fact]
    public void StackOverflow_WithoutExceptionInfo_FallsBackToTheDescription()
    {
        const string text = "Application: Apex.UI.exe\nDescription: The process was terminated due to stack overflow.";
        Assert.Equal("The process was terminated due to stack overflow.", WindowsCrashRecords.Summarize(text));
    }

    [Fact]
    public void OtherApplications_AreIgnored() =>
        Assert.False(WindowsCrashRecords.MentionsApex("Faulting application name: chrome.exe, version: 1.0"));
}

public sealed class ProblemReportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "apex-report-" + Guid.NewGuid().ToString("N"));
    private string Local => Path.Combine(_root, "local");
    private string Data => Path.Combine(_root, "data");

    public ProblemReportTests()
    {
        Directory.CreateDirectory(Path.Combine(Local, "logs"));
        Directory.CreateDirectory(Path.Combine(Data, "checkpoints"));
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private static void Touch(string path, string text, DateTime written)
    {
        File.WriteAllText(path, text);
        File.SetLastWriteTime(path, written);
    }

    [Fact]
    public void Collects_RecentLogs_Register_AndCheckpoints_ButNeverTheLicence()
    {
        var now = new DateTime(2026, 9, 14, 12, 0, 0);
        Touch(Path.Combine(Local, "crash_log.txt"), "crash", now.AddDays(-1));
        Touch(Path.Combine(Local, "logs", "apex-printing-20260914.log"), "today", now.AddHours(-1));
        Touch(Path.Combine(Local, "logs", "apex-printing-20260801.log"), "old", now.AddDays(-40));
        Touch(Path.Combine(Local, "license.apex"), "signed licence", now);
        // The register and checkpoints matter however old they are.
        Touch(Path.Combine(Data, "number-registry.jsonl"), "{}", now.AddDays(-90));
        Touch(Path.Combine(Data, "checkpoints", "a1b2c3.json"), "{}", now.AddDays(-90));

        var entries = ProblemReport.CollectFiles(Local, Data, now).Select(f => f.Entry).ToList();

        Assert.Contains("app/crash_log.txt", entries);
        Assert.Contains("logs/apex-printing-20260914.log", entries);
        Assert.DoesNotContain("logs/apex-printing-20260801.log", entries);
        Assert.Contains("numbering/number-registry.jsonl", entries);
        Assert.Contains("numbering/checkpoints/a1b2c3.json", entries);
        Assert.DoesNotContain(entries, e => e.EndsWith(".apex", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Write_ReadsALogTheLoggerStillHasOpen()
    {
        var log = Path.Combine(Local, "logs", "live.log");
        using var logger = new FileStream(log, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        logger.Write("still writing"u8);
        logger.Flush();

        var zip = Path.Combine(_root, "out", "report.zip");
        ProblemReport.Write(zip, "summary here", "no events", new[] { ("logs/live.log", log) });

        using var archive = ZipFile.OpenRead(zip);
        Assert.Equal("summary here", Read(archive, "summary.txt"));
        Assert.Equal("no events", Read(archive, "windows-events.txt"));
        Assert.Equal("still writing", Read(archive, "logs/live.log"));
        Assert.False(File.Exists(zip + ".partial"));
    }

    [Fact]
    public void DefaultFileName_SortsByTime() =>
        Assert.Equal("Apex-Report-20260914-0905.zip", ProblemReport.DefaultFileName(new DateTime(2026, 9, 14, 9, 5, 0)));

    private static string Read(ZipArchive archive, string name)
    {
        using var reader = new StreamReader(archive.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }
}
