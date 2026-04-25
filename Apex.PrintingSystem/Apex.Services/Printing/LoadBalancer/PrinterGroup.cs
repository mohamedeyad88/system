using System;
using System.Collections.Generic;
using System.Linq;
using Apex.Services.Printing.Resilience;

namespace Apex.Services.Printing.LoadBalancer
{
    public enum LoadBalancingStrategy
    {
        RoundRobin,      // Cycle through printers in order
        LeastLoaded,     // Send to printer with shortest queue
        HealthWeighted,  // Prefer healthiest printer (circuit breaker score)
        Fastest,         // Prefer printer type: Network > Local (by typical speed)
        Random           // Random selection (for testing)
    }

    public enum GroupCapability
    {
        ColorOnly,
        MonochromeOnly,
        Both
    }

    public class PrinterGroupMember
    {
        public string  PrinterName   { get; set; } = "";
        public bool    IsEnabled     { get; set; } = true;
        public int     Weight        { get; set; } = 1;     // For weighted strategies
        public int     MaxConcurrent { get; set; } = 1;     // Max simultaneous jobs
        public string? Notes         { get; set; }
    }

    public class PrinterGroup
    {
        public string                   Id          { get; set; } = Guid.NewGuid().ToString("N")[..10];
        public string                   Name        { get; set; } = "";
        public string                   Description { get; set; } = "";
        public LoadBalancingStrategy    Strategy    { get; set; } = LoadBalancingStrategy.LeastLoaded;
        public GroupCapability          Capability  { get; set; } = GroupCapability.Both;
        public List<PrinterGroupMember> Members     { get; set; } = new();
        public bool                     IsEnabled   { get; set; } = true;
        public bool                     IsDefault   { get; set; } = false; // Default group for unrouted jobs
        public DateTime                 CreatedAt   { get; set; } = DateTime.Now;
        public string                   CreatedBy   { get; set; } = "";

        /// <summary>
        /// Returns enabled members whose circuit breaker is not open.
        /// </summary>
        public List<PrinterGroupMember> GetActiveMembers()
        {
            return Members
                .Where(m => m.IsEnabled &&
                            CircuitBreakerManager.Instance.CanExecute(m.PrinterName))
                .ToList();
        }

        public bool CanHandleColorJob()  => Capability != GroupCapability.MonochromeOnly;
        public bool CanHandleMonoJob()   => Capability != GroupCapability.ColorOnly;
    }
}
