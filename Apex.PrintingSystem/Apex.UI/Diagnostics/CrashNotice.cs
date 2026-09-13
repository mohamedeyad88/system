using System;
using System.IO;
using System.Windows;
using Apex.Core.Diagnostics;
using Apex.UI.ViewModels;

namespace Apex.UI.Diagnostics
{
    /// <summary>
    /// Tells the operator, at the next start, that the last session died — and why, if
    /// Windows knows. The cause is also written to logs\last-crash.txt so it travels in
    /// the problem report even if nobody reads the dialog.
    /// </summary>
    public static class CrashNotice
    {
        public static void Show(Window owner, PreviousSession previous)
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

            var answer = MessageBox.Show(owner,
                string.Format(ViewModelBase.L("Crash_PreviousSession"), started, reason),
                ViewModelBase.L("Crash_PreviousSessionTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (answer == MessageBoxResult.Yes)
                _ = ProblemReport.SaveInteractiveAsync(owner);
        }
    }
}
