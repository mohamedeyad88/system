using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Apex.Services.Users
{
    /// <summary>
    /// Stores the aggregate print usage for a single user on a given calendar day.
    /// </summary>
    public class QuotaUsageRecord
    {
        public string   UserId       { get; set; } = "";
        public DateTime Date         { get; set; }   // Date component only (time = midnight)
        public int      PagesUsed    { get; set; }
        public int      JobsCount    { get; set; }
        public bool     HadColorJobs { get; set; }
    }

    /// <summary>
    /// Result returned by <see cref="PrintQuotaManager.CheckQuota"/>.
    /// </summary>
    public class QuotaCheckResult
    {
        public bool   Allowed        { get; set; }
        public string ReasonArabic   { get; set; } = "";
        public int    RemainingPages { get; set; }  // -1 = unlimited
        public int    RemainingJobs  { get; set; }  // -1 = unlimited
        public double UsagePercent   { get; set; }  // 0-100
    }

    /// <summary>
    /// Singleton that tracks per-user daily and monthly page/job usage and
    /// enforces the quotas defined on each <see cref="UserAccount"/>.
    /// </summary>
    public class PrintQuotaManager
    {
        // ── Singleton ─────────────────────────────────────────────────────────
        public static readonly PrintQuotaManager Instance = new();

        // ── State ─────────────────────────────────────────────────────────────
        private readonly string _usageFilePath;

        /// <summary>
        /// Key = userId; Value = ordered list of daily usage records (one per calendar day).
        /// </summary>
        private readonly ConcurrentDictionary<string, List<QuotaUsageRecord>> _usage = new();

        private readonly object _saveLock = new();

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            Converters    = { new JsonStringEnumConverter() }
        };

        /// <summary>Raised when a quota violation is detected (job denied).</summary>
        public event EventHandler<(string UserId, QuotaCheckResult Result)>? QuotaViolationAttempted;

        // ── Constructor ───────────────────────────────────────────────────────
        private PrintQuotaManager()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Apex", "Users");

            Directory.CreateDirectory(dir);
            _usageFilePath = Path.Combine(dir, "quota-usage.json");

            Load();
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Checks whether <paramref name="userId"/> is allowed to print
        /// <paramref name="requestedPages"/> pages (optionally in color).
        /// </summary>
        public QuotaCheckResult CheckQuota(string userId, int requestedPages, bool isColor)
        {
            // 1. Look up user
            var user = UserAccountRepository.Instance.GetById(userId);
            if (user == null)
                return Deny("مستخدم غير معروف");

            // 2. Account disabled
            if (!user.IsActive)
                return Deny("الحساب موقوف");

            // 3. Color restriction
            if (isColor && !user.ColorPrintAllowed)
                return Deny("الطباعة الملونة غير مسموحة لهذا الحساب");

            var today = DateTime.Today;
            var (todayPages, todayJobs, monthPages, _) = GetUsageSummary(userId);

            // 4. Daily page quota
            if (user.DailyPageQuota.HasValue)
            {
                int quota     = user.DailyPageQuota.Value;
                int projected = todayPages + requestedPages;
                if (projected > quota)
                {
                    int remaining = Math.Max(0, quota - todayPages);
                    var result = Deny($"تجاوزت الحد اليومي للصفحات ({quota} صفحة). المتبقي: {remaining}");
                    result.RemainingPages = remaining;
                    result.UsagePercent   = quota > 0 ? Math.Min(100.0, todayPages * 100.0 / quota) : 100;
                    QuotaViolationAttempted?.Invoke(this, (userId, result));
                    return result;
                }
            }

            // 5. Daily job quota
            if (user.DailyJobQuota.HasValue)
            {
                int quota = user.DailyJobQuota.Value;
                if (todayJobs >= quota)
                {
                    var result = Deny($"تجاوزت الحد اليومي لعدد المهام ({quota} مهمة).");
                    result.RemainingJobs = 0;
                    result.UsagePercent  = 100;
                    QuotaViolationAttempted?.Invoke(this, (userId, result));
                    return result;
                }
            }

            // 6. Monthly page quota
            if (user.MonthlyPageQuota.HasValue)
            {
                int quota     = user.MonthlyPageQuota.Value;
                int projected = monthPages + requestedPages;
                if (projected > quota)
                {
                    int remaining = Math.Max(0, quota - monthPages);
                    var result = Deny($"تجاوزت الحد الشهري للصفحات ({quota} صفحة). المتبقي: {remaining}");
                    result.RemainingPages = remaining;
                    result.UsagePercent   = quota > 0 ? Math.Min(100.0, monthPages * 100.0 / quota) : 100;
                    QuotaViolationAttempted?.Invoke(this, (userId, result));
                    return result;
                }
            }

            // 7. Allowed — calculate remaining counts
            int remainingPagesResult = -1;
            int remainingJobsResult  = -1;
            double usagePct          = 0;

            if (user.DailyPageQuota.HasValue)
            {
                int q = user.DailyPageQuota.Value;
                remainingPagesResult = Math.Max(0, q - todayPages - requestedPages);
                usagePct = q > 0 ? Math.Min(100.0, (todayPages + requestedPages) * 100.0 / q) : 0;
            }
            else if (user.MonthlyPageQuota.HasValue)
            {
                int q = user.MonthlyPageQuota.Value;
                remainingPagesResult = Math.Max(0, q - monthPages - requestedPages);
                usagePct = q > 0 ? Math.Min(100.0, (monthPages + requestedPages) * 100.0 / q) : 0;
            }

            if (user.DailyJobQuota.HasValue)
                remainingJobsResult = Math.Max(0, user.DailyJobQuota.Value - todayJobs - 1);

            return new QuotaCheckResult
            {
                Allowed        = true,
                ReasonArabic   = "",
                RemainingPages = remainingPagesResult,
                RemainingJobs  = remainingJobsResult,
                UsagePercent   = Math.Round(usagePct, 1)
            };
        }

        /// <summary>
        /// Records completed-job usage for <paramref name="userId"/>.
        /// Increments or creates today's <see cref="QuotaUsageRecord"/> and persists.
        /// </summary>
        public void RecordUsage(string userId, int pagesUsed, bool hadColor)
        {
            if (string.IsNullOrWhiteSpace(userId)) return;
            if (pagesUsed <= 0) pagesUsed = 1;

            var today = DateTime.Today;

            var records = _usage.GetOrAdd(userId, _ => new List<QuotaUsageRecord>());

            lock (records)
            {
                var rec = records.FirstOrDefault(r => r.Date == today);
                if (rec == null)
                {
                    rec = new QuotaUsageRecord { UserId = userId, Date = today };
                    records.Add(rec);
                }

                rec.PagesUsed    += pagesUsed;
                rec.JobsCount    += 1;
                rec.HadColorJobs  = rec.HadColorJobs || hadColor;
            }

            Save();
        }

        /// <summary>
        /// Returns today's page/job counts and this month's page/job totals for
        /// <paramref name="userId"/>.
        /// </summary>
        public (int TodayPages, int TodayJobs, int MonthPages, int MonthJobs) GetUsageSummary(string userId)
        {
            if (!_usage.TryGetValue(userId, out var records))
                return (0, 0, 0, 0);

            var today       = DateTime.Today;
            var monthStart  = new DateTime(today.Year, today.Month, 1);

            lock (records)
            {
                var todayRec  = records.FirstOrDefault(r => r.Date == today);
                var monthRecs = records.Where(r => r.Date >= monthStart).ToList();

                return (
                    TodayPages : todayRec?.PagesUsed ?? 0,
                    TodayJobs  : todayRec?.JobsCount  ?? 0,
                    MonthPages : monthRecs.Sum(r => r.PagesUsed),
                    MonthJobs  : monthRecs.Sum(r => r.JobsCount)
                );
            }
        }

        /// <summary>
        /// Returns all users whose daily or monthly usage has exceeded 80 % of their quota.
        /// </summary>
        public List<(UserAccount User, double DailyUsagePercent, double MonthlyUsagePercent)> GetQuotaWarnings()
        {
            var warnings = new List<(UserAccount, double, double)>();

            foreach (var user in UserAccountRepository.Instance.GetAll())
            {
                var (todayPages, _, monthPages, _) = GetUsageSummary(user.Id);

                double dailyPct   = 0;
                double monthlyPct = 0;

                if (user.DailyPageQuota.HasValue && user.DailyPageQuota.Value > 0)
                    dailyPct = todayPages * 100.0 / user.DailyPageQuota.Value;

                if (user.MonthlyPageQuota.HasValue && user.MonthlyPageQuota.Value > 0)
                    monthlyPct = monthPages * 100.0 / user.MonthlyPageQuota.Value;

                if (dailyPct > 80 || monthlyPct > 80)
                    warnings.Add((user, Math.Round(dailyPct, 1), Math.Round(monthlyPct, 1)));
            }

            return warnings;
        }

        /// <summary>
        /// Removes all of today's usage records (intended to be called at midnight).
        /// </summary>
        public void ResetDailyUsage()
        {
            var today = DateTime.Today;

            foreach (var records in _usage.Values)
            {
                lock (records)
                {
                    records.RemoveAll(r => r.Date == today);
                }
            }

            Save();
        }

        /// <summary>
        /// Returns historical usage records for <paramref name="userId"/> covering
        /// the last <paramref name="days"/> calendar days (default 30).
        /// </summary>
        public List<QuotaUsageRecord> GetUserHistory(string userId, int days = 30)
        {
            if (!_usage.TryGetValue(userId, out var records))
                return new List<QuotaUsageRecord>();

            var cutoff = DateTime.Today.AddDays(-(days - 1));

            lock (records)
            {
                return records
                    .Where(r => r.Date >= cutoff)
                    .OrderByDescending(r => r.Date)
                    .ToList();
            }
        }

        // ── Persistence ───────────────────────────────────────────────────────

        private void Save()
        {
            lock (_saveLock)
            {
                // Flatten to a list of all records across all users
                var allRecords = _usage.Values
                    .SelectMany(list => { lock (list) return list.ToList(); })
                    .ToList();

                var json = JsonSerializer.Serialize(allRecords, _jsonOptions);
                var tmp  = _usageFilePath + ".tmp";
                File.WriteAllText(tmp, json, System.Text.Encoding.UTF8);
                File.Move(tmp, _usageFilePath, overwrite: true);
            }
        }

        private void Load()
        {
            if (!File.Exists(_usageFilePath)) return;

            try
            {
                var json    = File.ReadAllText(_usageFilePath, System.Text.Encoding.UTF8);
                var records = JsonSerializer.Deserialize<List<QuotaUsageRecord>>(json, _jsonOptions);

                if (records == null) return;

                foreach (var rec in records)
                {
                    // Normalise to date-only (strip time component if any)
                    rec.Date = rec.Date.Date;

                    var list = _usage.GetOrAdd(rec.UserId, _ => new List<QuotaUsageRecord>());
                    lock (list) list.Add(rec);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PrintQuotaManager] Failed to load usage data: {ex.Message}");
                // Start fresh — non-fatal
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static QuotaCheckResult Deny(string reasonArabic)
            => new() { Allowed = false, ReasonArabic = reasonArabic, RemainingPages = 0, RemainingJobs = 0, UsagePercent = 100 };
    }
}
