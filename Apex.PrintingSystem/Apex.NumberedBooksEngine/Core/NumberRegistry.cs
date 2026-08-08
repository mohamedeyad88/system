using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Apex.NumberedBooksEngine.Core
{
    /// <summary>One issued range of numbers — an audit record, never edited in place.</summary>
    public sealed record NumberRangeRecord(
        string Id,
        string Series,
        long StartNumber,
        long EndNumber,        // inclusive
        DateTime PrintedUtc,
        string PrinterName,
        string Operator,
        string JobId,
        string Notes)
    {
        public long Count => EndNumber - StartNumber + 1;

        /// <summary>True when this record shares any number with [<paramref name="start"/>, <paramref name="end"/>].</summary>
        public bool Overlaps(long start, long end) => StartNumber <= end && start <= EndNumber;
    }

    public enum RangeCheckStatus
    {
        /// <summary>The whole range is free — safe to print.</summary>
        Available,

        /// <summary>Part or all of the range was already issued.</summary>
        Overlaps,

        /// <summary>The range itself is nonsensical (non-positive count, overflow…).</summary>
        Invalid
    }

    public sealed record RangeCheckResult(
        RangeCheckStatus Status,
        IReadOnlyList<NumberRangeRecord> Conflicts,
        string Message)
    {
        public bool IsAvailable => Status == RangeCheckStatus.Available;
    }

    /// <summary>
    /// The register of numbers this installation has already issued.
    ///
    /// Numbered books are legal documents: an invoice or receipt number printed
    /// twice is a compliance breach for both the press and its customer, and a gap
    /// has to be explainable to an auditor. The engine could previously reprint the
    /// same range indefinitely with nothing to detect it — this class closes that.
    ///
    /// Records are append-only JSON Lines: an audit trail must not be silently
    /// rewritten, and a corrupt line can be skipped without losing the rest.
    /// Ranges are tracked per SERIES (the number prefix), so "INV-…" and "REC-…"
    /// are independent sequences.
    /// </summary>
    public sealed class NumberRegistry
    {
        private readonly string _path;
        private readonly object _gate = new();

        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

        /// <param name="filePath">
        /// Ledger location. Defaults to a per-machine file under ProgramData so the
        /// register survives per-user profiles and app reinstalls.
        /// </param>
        public NumberRegistry(string? filePath = null)
        {
            _path = filePath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "ApexPrintingSystem", "number-registry.jsonl");

            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }

        // ── Queries ───────────────────────────────────────────────────────────

        /// <summary>
        /// Is [start, start+count-1] free in <paramref name="series"/>?
        /// Call BEFORE printing; the answer lists every conflicting record so the
        /// operator can see who used the numbers and when.
        /// </summary>
        public RangeCheckResult Check(string? series, long start, long count)
        {
            if (count <= 0)
                return new RangeCheckResult(RangeCheckStatus.Invalid, Array.Empty<NumberRangeRecord>(),
                    "عدد الأرقام يجب أن يكون أكبر من صفر.");
            if (start < 0)
                return new RangeCheckResult(RangeCheckStatus.Invalid, Array.Empty<NumberRangeRecord>(),
                    "رقم البداية لا يمكن أن يكون سالبًا.");
            if (start > long.MaxValue - count)
                return new RangeCheckResult(RangeCheckStatus.Invalid, Array.Empty<NumberRangeRecord>(),
                    "نطاق الأرقام يتجاوز الحد الأقصى.");

            long end = start + count - 1;
            string key = Normalize(series);

            var conflicts = All()
                .Where(r => Normalize(r.Series) == key && r.Overlaps(start, end))
                .OrderBy(r => r.StartNumber)
                .ToList();

            if (conflicts.Count == 0)
                return new RangeCheckResult(RangeCheckStatus.Available, conflicts, "النطاق متاح.");

            var first = conflicts[0];
            return new RangeCheckResult(
                RangeCheckStatus.Overlaps,
                conflicts,
                $"الأرقام من {first.StartNumber} إلى {first.EndNumber} سبق طباعتها بتاريخ " +
                $"{first.PrintedUtc.ToLocalTime():yyyy-MM-dd} على «{first.PrinterName}»." +
                (conflicts.Count > 1 ? $" (و{conflicts.Count - 1} نطاق آخر متعارض)" : ""));
        }

        /// <summary>Every record, newest first. Corrupt lines are skipped.</summary>
        public IReadOnlyList<NumberRangeRecord> All()
        {
            lock (_gate)
            {
                if (!File.Exists(_path)) return Array.Empty<NumberRangeRecord>();

                var list = new List<NumberRangeRecord>();
                foreach (var line in ReadLinesSafe())
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var rec = JsonSerializer.Deserialize<NumberRangeRecord>(line, JsonOpts);
                        if (rec != null) list.Add(rec);
                    }
                    catch { /* skip a damaged line rather than lose the ledger */ }
                }
                return list.OrderByDescending(r => r.PrintedUtc).ToList();
            }
        }

        /// <summary>All records of one series, ascending by start number.</summary>
        public IReadOnlyList<NumberRangeRecord> GetSeries(string? series)
        {
            string key = Normalize(series);
            return All().Where(r => Normalize(r.Series) == key)
                        .OrderBy(r => r.StartNumber)
                        .ToList();
        }

        /// <summary>
        /// Unused stretches between issued ranges. An auditor asks about missing
        /// numbers, so the operator needs to see them before the question is asked.
        /// </summary>
        public IReadOnlyList<(long From, long To)> FindGaps(string? series)
        {
            var ranges = GetSeries(series);
            var gaps = new List<(long, long)>();
            if (ranges.Count == 0) return gaps;

            long cursor = ranges[0].EndNumber;
            foreach (var r in ranges.Skip(1))
            {
                if (r.StartNumber > cursor + 1)
                    gaps.Add((cursor + 1, r.StartNumber - 1));
                if (r.EndNumber > cursor) cursor = r.EndNumber;
            }
            return gaps;
        }

        /// <summary>The first number after everything already issued in the series.</summary>
        public long NextAvailable(string? series)
        {
            var ranges = GetSeries(series);
            return ranges.Count == 0 ? 1 : ranges.Max(r => r.EndNumber) + 1;
        }

        // ── Recording ─────────────────────────────────────────────────────────

        /// <summary>
        /// Records a printed range. Call AFTER the job actually printed — recording
        /// up front would burn the numbers on a job that failed or was cancelled.
        /// </summary>
        public NumberRangeRecord Record(
            string? series, long start, long count,
            string printerName, string? jobId = null, string? notes = null)
        {
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));

            var rec = new NumberRangeRecord(
                Id: Guid.NewGuid().ToString("N"),
                Series: Normalize(series),
                StartNumber: start,
                EndNumber: start + count - 1,
                PrintedUtc: DateTime.UtcNow,
                PrinterName: printerName ?? "",
                Operator: SafeUserName(),
                JobId: jobId ?? "",
                Notes: notes ?? "");

            lock (_gate)
            {
                File.AppendAllText(_path,
                    JsonSerializer.Serialize(rec, JsonOpts) + Environment.NewLine);
            }
            return rec;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>Series keys compare case- and whitespace-insensitively.</summary>
        private static string Normalize(string? series) =>
            (series ?? "").Trim().ToUpperInvariant();

        private IEnumerable<string> ReadLinesSafe()
        {
            try { return File.ReadAllLines(_path); }
            catch { return Array.Empty<string>(); }
        }

        private static string SafeUserName()
        {
            try { return Environment.UserName ?? ""; }
            catch { return ""; }
        }

        /// <summary>Formats a range for display, e.g. "INV 000001–000500".</summary>
        public static string Describe(NumberRangeRecord r, NumberFormatOptions? fmt = null)
        {
            var o = fmt ?? NumberFormatOptions.Default;
            string s = NumberFormatter.Format(r.StartNumber, o);
            string e = NumberFormatter.Format(r.EndNumber, o);
            string when = r.PrintedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            return $"{s} – {e}  ({r.Count})  {when}  {r.PrinterName}";
        }
    }
}
