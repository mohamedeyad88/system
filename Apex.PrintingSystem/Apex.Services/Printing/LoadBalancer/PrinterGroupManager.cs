using Apex.Services.Printing.Queue;
using Apex.Services.Printing.VendorDetection;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Apex.Services.Printing.LoadBalancer
{
    public class RoutingDecision
    {
        public string   JobId           { get; set; } = "";
        public string   RequestedGroup  { get; set; } = "";
        public string   SelectedPrinter { get; set; } = "";
        public string   Strategy        { get; set; } = "";
        public int      CandidateCount  { get; set; }
        public bool     WasFallback     { get; set; }
        public string   Reason          { get; set; } = "";
        public DateTime DecidedAt       { get; set; } = DateTime.Now;
    }

    public class PrinterGroupManager
    {
        public static readonly PrinterGroupManager Instance = new();

        private readonly string _filePath;
        private readonly ConcurrentDictionary<string, PrinterGroup> _groups = new();
        private readonly List<RoutingDecision> _recentDecisions = new();
        private readonly object _decisionsLock = new();

        private PrinterGroupManager()
        {
            _filePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Apex", "PrinterGroups.json");
            Load();
        }

        // ── CRUD ──────────────────────────────────────────────────────

        public PrinterGroup AddGroup(PrinterGroup group)
        {
            _groups[group.Id] = group;
            Save();
            return group;
        }

        public void UpdateGroup(PrinterGroup group)
        {
            _groups[group.Id] = group;
            Save();
        }

        public bool DeleteGroup(string id)
        {
            var r = _groups.TryRemove(id, out _);
            if (r) Save();
            return r;
        }

        public IReadOnlyList<PrinterGroup> GetAll() =>
            _groups.Values.OrderByDescending(g => g.IsDefault).ThenBy(g => g.Name).ToList();

        public PrinterGroup? GetById(string id) =>
            _groups.TryGetValue(id, out var g) ? g : null;

        public PrinterGroup? GetDefault() =>
            _groups.Values.FirstOrDefault(g => g.IsDefault && g.IsEnabled);

        public PrinterGroup? GetByName(string name) =>
            _groups.Values.FirstOrDefault(g =>
                g.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        // ── Routing ───────────────────────────────────────────────────

        public async Task<RoutingDecision> RouteJobAsync(
            string jobId,
            string? groupNameOrId = null,
            bool isColorJob = false)
        {
            var decision = new RoutingDecision { JobId = jobId, RequestedGroup = groupNameOrId ?? "" };

            // Find target group
            PrinterGroup? group = null;
            if (!string.IsNullOrEmpty(groupNameOrId))
            {
                group = GetById(groupNameOrId) ?? GetByName(groupNameOrId);
            }
            group ??= GetDefault();

            if (group == null || !group.IsEnabled)
            {
                decision.Reason = "لا توجد مجموعة طباعة متاحة";
                Debug.WriteLine($"[LB] No group available for job {jobId}");
                return decision;
            }

            decision.Strategy        = group.Strategy.ToString();
            decision.CandidateCount  = group.GetActiveMembers().Count;

            // Try primary group
            string? selected = await LoadBalancerStrategy.SelectPrinterAsync(group, isColorJob);

            // Fallback to other enabled groups
            if (selected == null)
            {
                foreach (var fallbackGroup in _groups.Values
                    .Where(g => g.Id != group.Id && g.IsEnabled)
                    .OrderByDescending(g => g.IsDefault))
                {
                    selected = await LoadBalancerStrategy.SelectPrinterAsync(fallbackGroup, isColorJob);
                    if (selected != null)
                    {
                        decision.WasFallback = true;
                        decision.Strategy    = fallbackGroup.Strategy.ToString();
                        decision.Reason      = $"احتياطي من مجموعة '{fallbackGroup.Name}'";
                        break;
                    }
                }
            }

            if (selected == null)
            {
                decision.Reason = "جميع الطابعات غير متاحة حالياً";
            }
            else
            {
                decision.SelectedPrinter = selected;
                if (string.IsNullOrEmpty(decision.Reason))
                    decision.Reason = $"تم الاختيار من '{group.Name}'";
            }

            // Record decision (max 100)
            lock (_decisionsLock)
            {
                _recentDecisions.Insert(0, decision);
                while (_recentDecisions.Count > 100) _recentDecisions.RemoveAt(_recentDecisions.Count - 1);
            }

            JobRouted?.Invoke(this, decision);
            return decision;
        }

        public async Task<string> SubmitRoutedJobAsync(
            string filePath,
            string? groupNameOrId = null,
            int copies = 1,
            bool isColorJob = false,
            bool useRawMode = true)
        {
            var decision = await RouteJobAsync("", groupNameOrId, isColorJob);

            if (string.IsNullOrEmpty(decision.SelectedPrinter))
                throw new InvalidOperationException(decision.Reason);

            var jobId = await PrintJobQueueManager.Instance.SubmitPrintJobAsync(
                decision.SelectedPrinter,
                filePath,
                copies);

            decision.JobId = jobId;
            return jobId;
        }

        public IReadOnlyList<RoutingDecision> GetRecentDecisions(int count = 20)
        {
            lock (_decisionsLock)
                return _recentDecisions.Take(count).ToList();
        }

        public async Task CreateDefaultGroupFromInstalledPrintersAsync()
        {
            var printers = await VendorDetectionEngine.Instance.DetectAllPrintersAsync();
            var members  = printers.Select(p => new PrinterGroupMember
            {
                PrinterName   = p.Name,
                IsEnabled     = true,
                Weight        = 1,
                MaxConcurrent = 1
            }).ToList();

            var group = new PrinterGroup
            {
                Name       = "الطابعات الافتراضية",
                Description = "جميع الطابعات المثبتة",
                Strategy   = LoadBalancingStrategy.LeastLoaded,
                Capability = GroupCapability.Both,
                IsDefault  = true,
                IsEnabled  = true,
                Members    = members
            };

            AddGroup(group);
            Debug.WriteLine($"[LB] Default group created with {members.Count} printers");
        }

        public (int TotalMembers, int ActiveMembers, int TotalRoutedJobs) GetGroupStats(string groupId)
        {
            if (!_groups.TryGetValue(groupId, out var group))
                return (0, 0, 0);

            int total  = group.Members.Count;
            int active = group.GetActiveMembers().Count;
            int routed;
            lock (_decisionsLock)
                routed = _recentDecisions.Count(d => d.RequestedGroup == groupId);

            return (total, active, routed);
        }

        public event EventHandler<RoutingDecision>? JobRouted;

        // ── Persistence ───────────────────────────────────────────────

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
                var json    = JsonSerializer.Serialize(_groups.Values.ToList(),
                    new JsonSerializerOptions { WriteIndented = true });
                var tmpPath = _filePath + ".tmp";
                File.WriteAllText(tmpPath, json, System.Text.Encoding.UTF8);
                File.Move(tmpPath, _filePath, overwrite: true);
            }
            catch (Exception ex) { Debug.WriteLine($"[LB] Save error: {ex.Message}"); }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_filePath)) return;
                var list = JsonSerializer.Deserialize<List<PrinterGroup>>(
                    File.ReadAllText(_filePath));
                if (list == null) return;
                foreach (var g in list) _groups[g.Id] = g;
                Debug.WriteLine($"[LB] Loaded {_groups.Count} printer groups");
            }
            catch (Exception ex) { Debug.WriteLine($"[LB] Load error: {ex.Message}"); }
        }
    }
}
