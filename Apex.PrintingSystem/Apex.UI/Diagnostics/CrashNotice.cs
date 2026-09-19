using System;
using System.IO;
using System.Windows;
using Apex.Core.Diagnostics;
using Apex.UI.ViewModels;

namespace Apex.UI.Diagnostics
{
    /// <summary>
    /// Records, at the next start, that the last session died — and why, if Windows
    /// knows — into logs\last-crash.txt, so the cause travels in the problem report.
    ///
    /// It used to open a dialog about it on startup. The owner had it removed: a shop
    /// opening the till in the morning is greeted by a wall of Arabic about a session
    /// that failed for an ordinary reason (power cut, Task Manager, a hard shutdown),
    /// can do nothing useful with it, and learns to click past every dialog Apex shows.
    /// Support reads the same facts out of the report, which the Performance screen can
    /// produce on demand.
    /// </summary>
    public static class CrashNotice
    {
        public static void Record(PreviousSession previous)
        {
            // Look a minute before the recorded start so clock rounding cannot hide the entry.
            var since = (previous.StartedUtc ?? DateTime.UtcNow.AddDays(-1)).AddMinutes(-1);
            var records = WindowsCrashRecords.Since(since);
            var primary = WindowsCrashRecords.Primary(records);
            var reason = primary?.Summary ?? ViewModelBase.L("Crash_NoWindowsRecord");
            var started = previous.StartedUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "?";

            try
            {
                var dir = Path.Combine(ProblemReport.LocalAppDir, "logs");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "last-crash.txt"),
                    $"Session started {started} (version {previous.Version}) and did not close.{Environment.NewLine}" +
                    $"Noticed {DateTime.Now:yyyy-MM-dd HH:mm:ss}.{Environment.NewLine}{Environment.NewLine}" +
                    WindowsCrashRecords.Format(records));
            }
            catch { }
            AppDiagnostics.LogWarning("previous-session", $"started {started}, version {previous.Version}, did not close: {reason}");
        }
    }
}
