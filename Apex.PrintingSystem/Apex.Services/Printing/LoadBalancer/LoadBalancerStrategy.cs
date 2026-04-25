using Apex.Services.Printing.Resilience;
using Apex.Services.Printing.VendorDetection;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Apex.Services.Printing.LoadBalancer
{
    public static class LoadBalancerStrategy
    {
        // Tracks last-used index per group for RoundRobin (key = groupId)
        private static readonly ConcurrentDictionary<string, int> _rrCounters = new();

        /// <summary>
        /// Select the best printer from a group for a job.
        /// Returns null if no printer is available.
        /// </summary>
        public static async Task<string?> SelectPrinterAsync(
            PrinterGroup group,
            bool isColorJob = false)
        {
            // Filter active + capability-compatible members
            var candidates = group.GetActiveMembers()
                .Where(m => isColorJob ? group.CanHandleColorJob() : group.CanHandleMonoJob())
                .ToList();

            if (candidates.Count == 0)
            {
                Debug.WriteLine($"[LB] No active candidates in group '{group.Name}'");
                return null;
            }

            string? selected = group.Strategy switch
            {
                LoadBalancingStrategy.RoundRobin     => RoundRobin(group, candidates),
                LoadBalancingStrategy.LeastLoaded    => await LeastLoaded(group, candidates),
                LoadBalancingStrategy.HealthWeighted => HealthWeighted(group, candidates),
                LoadBalancingStrategy.Fastest        => await Fastest(group, candidates),
                LoadBalancingStrategy.Random         => RandomSelect(candidates),
                _                                   => RoundRobin(group, candidates)
            };

            Debug.WriteLine($"[LB] Group '{group.Name}' ({group.Strategy}) → '{selected}'");
            return selected;
        }

        // ── Strategies ────────────────────────────────────────────────

        private static string? RoundRobin(PrinterGroup group, List<PrinterGroupMember> candidates)
        {
            int next = _rrCounters.AddOrUpdate(
                group.Id,
                _ => 0,
                (_, prev) => (prev + 1) % candidates.Count);
            return candidates[next % candidates.Count].PrinterName;
        }

        private static async Task<string?> LeastLoaded(
            PrinterGroup group,
            List<PrinterGroupMember> candidates)
        {
            try
            {
                var printers = (await VendorDetectionEngine.Instance.DetectAllPrintersAsync())
                    .ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);

                var ranked = candidates
                    .Select(m =>
                    {
                        printers.TryGetValue(m.PrinterName, out var meta);
                        int queue   = 0; // QueueLength not available in metadata; use health score as tiebreaker
                        int health  = CircuitBreakerManager.Instance.GetHealthScore(m.PrinterName);
                        return (Member: m, Queue: queue, Health: health);
                    })
                    .OrderBy(x => x.Queue)
                    .ThenByDescending(x => x.Health)
                    .ToList();

                return ranked.FirstOrDefault().Member?.PrinterName;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LB] LeastLoaded fallback to RoundRobin: {ex.Message}");
                return RoundRobin(group, candidates);
            }
        }

        private static string? HealthWeighted(
            PrinterGroup group,
            List<PrinterGroupMember> candidates)
        {
            var weighted = candidates
                .Select(m => new
                {
                    m.PrinterName,
                    W = Math.Max(1, CircuitBreakerManager.Instance.GetHealthScore(m.PrinterName)) * m.Weight
                })
                .ToList();

            double total = weighted.Sum(x => x.W);
            if (total <= 0) return RoundRobin(group, candidates);

            double pick = System.Random.Shared.NextDouble() * total;
            double cumul = 0;
            foreach (var item in weighted)
            {
                cumul += item.W;
                if (pick <= cumul) return item.PrinterName;
            }
            return weighted.Last().PrinterName;
        }

        private static async Task<string?> Fastest(
            PrinterGroup group,
            List<PrinterGroupMember> candidates)
        {
            try
            {
                var printers = (await VendorDetectionEngine.Instance.DetectAllPrintersAsync())
                    .ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);

                return candidates
                    .Select(m =>
                    {
                        printers.TryGetValue(m.PrinterName, out var meta);
                        bool isOnline  = meta?.IsOnline ?? false;
                        int  queue     = 0; // QueueLength not available in metadata
                        bool isNetwork = meta?.IsNetworkPrinter ?? false;
                        return (m.PrinterName, isOnline, queue, isNetwork);
                    })
                    .OrderByDescending(x => x.isOnline)
                    .ThenBy(x => x.queue)
                    .ThenByDescending(x => x.isNetwork)
                    .FirstOrDefault().PrinterName;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LB] Fastest fallback: {ex.Message}");
                return RoundRobin(group, candidates);
            }
        }

        private static string? RandomSelect(List<PrinterGroupMember> candidates) =>
            candidates[System.Random.Shared.Next(candidates.Count)].PrinterName;
    }
}
