using System;
using System.Globalization;
using System.IO;

namespace Apex.UI.Diagnostics
{
    /// <summary>A session that started and never reached a clean exit.</summary>
    public sealed record PreviousSession(DateTime? StartedUtc, string Version);

    /// <summary>
    /// Notices that the last run of the app ended without closing.
    ///
    /// <para>The global exception handlers in App only see managed exceptions. A native
    /// crash — an <c>AccessViolationException</c> inside a renderer, a stack overflow —
    /// kills the process without running any of them, so the shop is left with no log and
    /// no idea what happened. That is exactly the kind of crash seen in the preview in the
    /// field, and it was found only by chance in the Windows event log.</para>
    ///
    /// <para>So the app drops a marker when it starts and removes it when it closes. A
    /// marker still present at the next start means the previous session died, whatever
    /// killed it; the caller then asks Windows why (<see cref="WindowsCrashRecords"/>).</para>
    /// </summary>
    public static class SessionGuard
    {
        public static string MarkerPath => Path.Combine(ProblemReport.LocalAppDir, "session.running");

        /// <summary>Starts watching this session; returns the previous one if it never closed.</summary>
        public static PreviousSession? Begin(string version) =>
            Begin(MarkerPath, version, DateTime.UtcNow, Environment.ProcessId);

        public static PreviousSession? Begin(string markerPath, string version, DateTime nowUtc, int processId)
        {
            PreviousSession? previous = null;
            try
            {
                if (File.Exists(markerPath))
                    previous = Parse(File.ReadAllLines(markerPath));
            }
            catch
            {
                // A marker we cannot read is still a marker: the last session did not close.
                previous = new PreviousSession(null, "?");
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
                File.WriteAllLines(markerPath, new[]
                {
                    nowUtc.ToString("o", CultureInfo.InvariantCulture),
                    version,
                    processId.ToString(CultureInfo.InvariantCulture),
                });
            }
            catch { /* watching a session must never stop the app starting */ }

            return previous;
        }

        /// <summary>Marks this session as closed cleanly.</summary>
        public static void End() => End(MarkerPath);

        public static void End(string markerPath)
        {
            try { File.Delete(markerPath); } catch { }
        }

        public static PreviousSession Parse(string[] lines)
        {
            DateTime? started = null;
            if (lines.Length > 0 &&
                DateTime.TryParse(lines[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t))
                started = t.ToUniversalTime();

            var version = lines.Length > 1 && !string.IsNullOrWhiteSpace(lines[1]) ? lines[1].Trim() : "?";
            return new PreviousSession(started, version);
        }
    }
}
