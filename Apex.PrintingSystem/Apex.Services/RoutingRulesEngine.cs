using Apex.Core.Interfaces;
using Apex.Core.Models;
using Apex.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Apex.Services
{
    public class RoutingRulesEngine : IRoutingRulesEngine
    {
        private readonly ApexDbContext _context;

        public RoutingRulesEngine(ApexDbContext context)
        {
            _context = context;
        }

        public async Task<RoutingRule?> EvaluateRulesAsync(PrintJob job)
        {
            var rules = await _context.RoutingRules.OrderByDescending(r => r.Priority).ToListAsync();

            foreach (var rule in rules)
            {
                if (MatchesCondition(rule.ConditionsJson, job))
                {
                    return rule;
                }
            }

            return null;
        }

        private bool MatchesCondition(string conditionsJson, PrintJob job)
        {
            try
            {
                var conditions = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(conditionsJson);
                if (conditions == null) return false;

                foreach (var kvp in conditions)
                {
                    if (kvp.Key == "extensions" && kvp.Value.ValueKind == JsonValueKind.Array)
                    {
                        var ext = System.IO.Path.GetExtension(job.OriginalFileName ?? job.FilePath).ToLowerInvariant();
                        var allowed = kvp.Value.EnumerateArray().Select(e => e.GetString()?.ToLowerInvariant());
                        if (!allowed.Contains(ext)) return false;
                    }

                    if (kvp.Key == "filenameContains" && kvp.Value.ValueKind == JsonValueKind.Array)
                    {
                        var name = (job.OriginalFileName ?? job.FilePath).ToLowerInvariant();
                        var terms = kvp.Value.EnumerateArray().Select(e => e.GetString()?.ToLowerInvariant());
                        if (!terms.Any(t => t != null && name.Contains(t))) return false;
                    }

                    if (kvp.Key == "minPages" && kvp.Value.ValueKind == JsonValueKind.Number)
                    {
                        if ((job.Pages ?? 0) < kvp.Value.GetInt32()) return false;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
