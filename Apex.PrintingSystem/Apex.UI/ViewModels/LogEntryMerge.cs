using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Apex.UI.ViewModels
{
    /// <summary>
    /// Merges the application log and the print log into one stream for the viewer.
    ///
    /// They are separate files in different formats, and the viewer only ever read
    /// the first — so after a successful print run the operator saw a single line
    /// about the monitoring service starting, and nothing about the job. The log
    /// built for diagnosing print failures was invisible in the one place someone
    /// would look for it.
    ///
    /// Two shapes have to be handled:
    ///   app   [07:36:54] [Info] [id] message
    ///   print 2026-08-16 12:03:28.490 [INF] message
    /// and the print log writes multi-line entries (a job header followed by its
    /// details), so continuation lines stay attached to the line above them.
    /// </summary>
    public static class LogEntryMerge
    {
        public sealed record Entry(TimeSpan Time, string Source, string Text);

        private const string AppSource = "APP";
        private const string PrintSource = "PRINT";

        /// <summary>Parses both files and returns their lines in time order.</summary>
        public static IReadOnlyList<Entry> Merge(string appLog, string printLog)
        {
            var entries = new List<Entry>();
            entries.AddRange(Parse(appLog, AppSource));
            entries.AddRange(Parse(printLog, PrintSource));

            // A stable sort keeps entries that share a timestamp in file order, so a
            // job header is never separated from the detail lines beneath it.
            return entries.OrderBy(e => e.Time).ToList();
        }

        /// <summary>Renders merged entries with a source tag the operator can scan.</summary>
        public static string Render(IEnumerable<Entry> entries)
        {
            var sb = new StringBuilder();
            foreach (var e in entries)
                sb.AppendLine($"[{e.Source,-5}] {e.Text}");
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// True when the entry matches the requested level, understanding both
        /// vocabularies: the app writes [Info]/[Warning]/[Error], Serilog writes
        /// [INF]/[WRN]/[ERR]. Filtering on one spelling hid the other file entirely.
        /// </summary>
        public static bool MatchesLevel(Entry entry, LogLevelFilter level)
        {
            if (level == LogLevelFilter.All) return true;

            foreach (var token in TokensFor(level))
                if (entry.Text.Contains($"[{token}]", StringComparison.OrdinalIgnoreCase))
                    return true;

            return false;
        }

        private static IEnumerable<string> TokensFor(LogLevelFilter level) => level switch
        {
            LogLevelFilter.Info     => new[] { "Info", "INF" },
            LogLevelFilter.Warning  => new[] { "Warning", "WRN" },
            LogLevelFilter.Error    => new[] { "Error", "ERR" },
            LogLevelFilter.Critical => new[] { "Critical", "FTL" },
            _ => Array.Empty<string>()
        };

        private static IEnumerable<Entry> Parse(string content, string source)
        {
            if (string.IsNullOrWhiteSpace(content)) yield break;

            var lines = content.Split('\n');
            var last = TimeSpan.Zero;

            foreach (var raw in lines)
            {
                var line = raw.TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(line)) continue;

                // A line without its own stamp continues the one above it — keep it
                // at the same instant so the sort cannot split the block.
                if (TryReadTime(line, out var t)) last = t;

                yield return new Entry(last, source, line);
            }
        }

        private static bool TryReadTime(string line, out TimeSpan time)
        {
            time = default;

            // print: 2026-08-16 12:03:28.490 …
            if (line.Length >= 23 && line[4] == '-' && line[10] == ' ')
            {
                if (TimeSpan.TryParseExact(line.Substring(11, 12), @"hh\:mm\:ss\.fff",
                        CultureInfo.InvariantCulture, out time))
                    return true;
            }

            // app: [07:36:54] …
            if (line.Length >= 10 && line[0] == '[' && line[9] == ']')
            {
                if (TimeSpan.TryParseExact(line.Substring(1, 8), @"hh\:mm\:ss",
                        CultureInfo.InvariantCulture, out time))
                    return true;
            }

            return false;
        }
    }
}
