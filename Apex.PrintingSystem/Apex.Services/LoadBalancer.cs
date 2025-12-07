using Apex.Core.Interfaces;
using Apex.Core.Models;
using System.Collections.Generic;
using System.Linq;

namespace Apex.Services
{
    public class LoadBalancer : ILoadBalancer
    {
        public PrinterInfo? SelectPrinterForJob(PrintJob job, IEnumerable<PrinterInfo> candidates)
        {
            // Filter candidates based on job requirements (e.g. pool) if needed
            // For now assume candidates are already filtered by pool

            return candidates
                .OrderByDescending(p => CalculateScore(p))
                .FirstOrDefault();
        }

        private double CalculateScore(PrinterInfo p)
        {
            // Score = (isOnline ? 1000 : 0)
            //       + 100 * (1.0 / (1 + queueLength))
            //       + weight * 50 (Assuming weight 1 for now as it's not in PrinterInfo yet)
            //       - errorRate * 200 (Assuming 0 for now)
            //       - currentActiveJobs * 10 (Using QueueLength as proxy)

            if (!p.IsOnline) return -1; // Offline printers are disqualified
            if (p.HasPaperJam || p.IsOutOfPaper || p.IsTonerLow) return 0; // Error state printers are last resort

            double score = 1000;
            score += 100.0 / (1 + p.QueueLength);
            score -= p.QueueLength * 10;

            return score;
        }
    }
}
