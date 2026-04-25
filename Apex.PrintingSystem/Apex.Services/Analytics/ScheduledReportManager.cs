using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.Services.Analytics
{
    public class ScheduledReportManager : IDisposable
    {
        public static readonly ScheduledReportManager Instance = new();

        private readonly string _definitionsPath;
        private readonly string _outputPath;
        private readonly ConcurrentDictionary<string, ReportDefinition> _definitions = new();
        private readonly CancellationTokenSource _cts = new();
        private Task? _schedulerTask;
        private bool _disposed;

        private ScheduledReportManager()
        {
            var baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Apex", "Reports");
            _definitionsPath = Path.Combine(baseDir, "schedules.json");
            _outputPath      = Path.Combine(baseDir, "Output");
            Directory.CreateDirectory(_outputPath);
            Load();
        }

        // ── CRUD ──────────────────────────────────────────────────────

        public ReportDefinition AddDefinition(ReportDefinition def)
        {
            def.OutputFolder = _outputPath;
            if (def.Schedule != ReportSchedule.None)
                def.NextRunAt = CalculateNextRun(def.Schedule, DateTime.Now);
            _definitions[def.Id] = def;
            Save();
            return def;
        }

        public void UpdateDefinition(ReportDefinition def)
        {
            _definitions[def.Id] = def;
            Save();
        }

        public bool DeleteDefinition(string id)
        {
            var removed = _definitions.TryRemove(id, out _);
            if (removed) Save();
            return removed;
        }

        public IReadOnlyList<ReportDefinition> GetAll() =>
            _definitions.Values.OrderBy(d => d.Name).ToList();

        public ReportDefinition? GetById(string id) =>
            _definitions.TryGetValue(id, out var d) ? d : null;

        // ── Scheduler ────────────────────────────────────────────────

        public void Start()
        {
            if (_schedulerTask != null) return;
            _schedulerTask = Task.Run(() => SchedulerLoopAsync(_cts.Token));
            Debug.WriteLine("[ScheduledReports] Scheduler started");
        }

        private async Task SchedulerLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var due = _definitions.Values
                        .Where(d => d.IsEnabled &&
                                    d.Schedule != ReportSchedule.None &&
                                    d.NextRunAt.HasValue &&
                                    d.NextRunAt.Value <= DateTime.Now)
                        .ToList();

                    foreach (var def in due)
                    {
                        try
                        {
                            await RunReportAsync(def);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[ScheduledReports] Error running '{def.Name}': {ex.Message}");
                        }
                    }

                    await Task.Delay(60_000, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ScheduledReports] Loop error: {ex.Message}");
                    await Task.Delay(60_000, ct);
                }
            }
        }

        public async Task<ReportResult> RunReportAsync(ReportDefinition def)
        {
            Debug.WriteLine($"[ScheduledReports] Running '{def.Name}'...");
            def.OutputFolder = _outputPath;

            var result = await Task.Run(() => ReportEngine.Instance.GenerateToFile(def));

            def.LastRunAt = DateTime.Now;
            if (def.Schedule != ReportSchedule.None)
                def.NextRunAt = CalculateNextRun(def.Schedule, DateTime.Now);
            UpdateDefinition(def);

            ReportGenerated?.Invoke(this, result);
            Debug.WriteLine($"[ScheduledReports] '{def.Name}' -> {(result.Success ? "OK" : "FAIL")} {result.FilePath}");
            return result;
        }

        private static DateTime? CalculateNextRun(ReportSchedule schedule, DateTime from) => schedule switch
        {
            ReportSchedule.Daily   => from.Date.AddDays(1).AddHours(7),
            ReportSchedule.Weekly  => NextWeekday(from, DayOfWeek.Monday).AddHours(7),
            ReportSchedule.Monthly => new DateTime(from.Year, from.Month, 1).AddMonths(1).AddHours(7),
            _                      => null
        };

        private static DateTime NextWeekday(DateTime from, DayOfWeek day)
        {
            int daysUntil = ((int)day - (int)from.DayOfWeek + 7) % 7;
            if (daysUntil == 0) daysUntil = 7;
            return from.Date.AddDays(daysUntil);
        }

        public event EventHandler<ReportResult>? ReportGenerated;

        public void EnsureDefaultSchedules()
        {
            if (_definitions.Any()) return;

            AddDefinition(new ReportDefinition
            {
                Name     = "\u0645\u0644\u062e\u0635 \u064a\u0648\u0645\u064a \u062a\u0644\u0642\u0627\u0626\u064a",
                Type     = ReportType.DailySummary,
                Format   = ReportFormat.PDF,
                Schedule = ReportSchedule.Daily,
                IsEnabled = true
            });

            AddDefinition(new ReportDefinition
            {
                Name     = "\u062a\u0642\u0631\u064a\u0631 \u0627\u0644\u0623\u0633\u0628\u0648\u0639",
                Type     = ReportType.WeeklySummary,
                Format   = ReportFormat.PDF,
                Schedule = ReportSchedule.Weekly,
                IsEnabled = true
            });
        }

        // ── Persistence ───────────────────────────────────────────────

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_definitionsPath)!);
                var json    = JsonSerializer.Serialize(_definitions.Values.ToList(),
                    new JsonSerializerOptions { WriteIndented = true });
                var tmpPath = _definitionsPath + ".tmp";
                File.WriteAllText(tmpPath, json, System.Text.Encoding.UTF8);
                File.Move(tmpPath, _definitionsPath, overwrite: true);
            }
            catch (Exception ex) { Debug.WriteLine($"[ScheduledReports] Save error: {ex.Message}"); }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_definitionsPath)) return;
                var list = JsonSerializer.Deserialize<List<ReportDefinition>>(
                    File.ReadAllText(_definitionsPath));
                if (list == null) return;
                foreach (var d in list) _definitions[d.Id] = d;
            }
            catch (Exception ex) { Debug.WriteLine($"[ScheduledReports] Load error: {ex.Message}"); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _cts.Cancel();
            _cts.Dispose();
            _disposed = true;
        }
    }
}
