using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Text;
using Apex.Core.Diagnostics;

namespace Apex.UI.Diagnostics
{
    /// <summary>One crash, hang or error report Windows recorded for this app.</summary>
    public sealed record CrashRecord(DateTime TimeLocal, string Provider, int EventId, string Summary, string Detail);

    /// <summary>
    /// Reads what Windows itself recorded when the app died.
    ///
    /// <para>A crash the process cannot catch still leaves up to three entries in the
    /// Application log: ".NET Runtime" 1026 (names the exception), "Application Error"
    /// 1000 (faulting module and exception code) and "Windows Error Reporting" 1001.
    /// Both executable names are matched — the installer renames Apex.UI.exe to
    /// ApexPrintOS.exe, so a filter on one would find nothing in the field.</para>
    /// </summary>
    public static class WindowsCrashRecords
    {
        private static readonly string[] ExeNames = { "Apex.UI.exe", "ApexPrintOS.exe" };

        public static IReadOnlyList<CrashRecord> Since(DateTime sinceUtc, int max = 10)
        {
            var found = new List<CrashRecord>();
            try
            {
                var xpath =
                    "*[System[Provider[@Name='.NET Runtime' or @Name='Application Error' or " +
                    "@Name='Application Hang' or @Name='Windows Error Reporting'] and " +
                    $"TimeCreated[@SystemTime>='{sinceUtc.ToUniversalTime():yyyy-MM-ddTHH:mm:ss.fffZ}']]]";

                var query = new EventLogQuery("Application", PathType.LogName, xpath) { ReverseDirection = true };
                using var reader = new EventLogReader(query);

                for (var record = reader.ReadEvent(); record != null && found.Count < max; record = reader.ReadEvent())
                {
                    using (record)
                    {
                        string text;
                        try { text = record.FormatDescription() ?? ""; } catch { text = ""; }
                        if (text.Length == 0)
                            text = string.Join(Environment.NewLine, record.Properties.Select(p => p.Value?.ToString()));

                        if (!MentionsApex(text)) continue;

                        found.Add(new CrashRecord(record.TimeCreated ?? DateTime.Now, record.ProviderName ?? "?",
                            record.Id, Summarize(text), text));
                    }
                }
            }
            catch (Exception ex)
            {
                AppDiagnostics.LogWarning("WindowsCrashRecords", ex);
            }
            return found;
        }

        public static bool MentionsApex(string text) =>
            ExeNames.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase));

        /// <summary>A one-line cause an operator can read out over the phone.</summary>
        public static string Summarize(string text)
        {
            string? Find(string label)
            {
                foreach (var raw in text.Split('\n'))
                {
                    var line = raw.Trim();
                    int i = line.IndexOf(label, StringComparison.OrdinalIgnoreCase);
                    if (i < 0) continue;
                    var value = line[(i + label.Length)..].Trim();
                    if (value.Length > 0) return value;
                }
                return null;
            }

            var parts = new List<string>();
            if (Find("Exception Info:") is { } exception) parts.Add(exception);
            if (Find("Faulting module name:") is { } module) parts.Add("module " + module.Split(',')[0].Trim());
            if (Find("Exception code:") is { } code) parts.Add("code " + code);
            if (parts.Count > 0) return Clip(string.Join(" · ", parts));

            if (Find("Description:") is { } description) return Clip(description);
            return Clip(text.Trim().Split('\n')[0].Trim());
        }

        /// <summary>The record that says most: the runtime's own report, then the fault report.</summary>
        public static CrashRecord? Primary(IReadOnlyList<CrashRecord> records) =>
            records.FirstOrDefault(r => r.Provider == ".NET Runtime")
            ?? records.FirstOrDefault(r => r.Provider == "Application Error")
            ?? records.FirstOrDefault();

        public static string Format(IReadOnlyList<CrashRecord> records)
        {
            if (records.Count == 0)
                return "No crash, hang or error report for Apex.UI.exe / ApexPrintOS.exe in the Windows Application log for this period.";

            var sb = new StringBuilder();
            foreach (var r in records)
            {
                sb.AppendLine($"=== {r.TimeLocal:yyyy-MM-dd HH:mm:ss}  {r.Provider} ({r.EventId})");
                sb.AppendLine(r.Summary);
                sb.AppendLine();
                sb.AppendLine(r.Detail.Trim());
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private static string Clip(string s) => s.Length > 200 ? s[..200] + "…" : s;
    }
}
