using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using Apex.Core.Enums;

namespace Apex.Core.Models
{
    /// <summary>
    /// Represents a single atomic Cycle (one number with all its copies).
    /// </summary>
    public class CycleJob
    {
        /// <summary>Unique identifier for this cycle job.</summary>
        public string JobId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>Cycle number (e.g., 1..N).</summary>
        public int CycleNumber { get; set; }

        /// <summary>Start number for this cycle (usually same as EndNumber).</summary>
        public long StartNumber { get; set; }

        /// <summary>End number for this cycle (inclusive).</summary>
        public long EndNumber { get; set; }

        /// <summary>Copies per page (Original + images).</summary>
        public int CopiesPerPage { get; set; } = 1;

        /// <summary>Pages belonging to this cycle (Original, Copy1, Copy2, ...).</summary>
        public List<CyclePage> Pages { get; set; } = new();

        /// <summary>Tray mapping per copy index (0=Original,1=Copy1,...).</summary>
        public Dictionary<int, PaperSourceKind> TrayMapping { get; set; } = new();

        /// <summary>Dependency on previous cycle job (JobId).</summary>
        public string? DependsOnJobId { get; set; }

        /// <summary>Current status of the cycle.</summary>
        public CycleStatus Status { get; set; } = CycleStatus.Pending;

        /// <summary>Batch identifier (for preparation batches).</summary>
        public string? BatchId { get; set; }

        /// <summary>Timestamps and error.</summary>
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? StartedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Helper: is dependency satisfied logically (CompletedPhysical or Skipped).
        /// </summary>
        public static bool IsDependencySatisfied(CycleStatus dependencyStatus)
            => dependencyStatus.IsCompletedLogical();
    }
}

