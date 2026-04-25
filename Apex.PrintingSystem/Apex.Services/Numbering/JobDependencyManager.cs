using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Apex.Core.Enums;
using Apex.Core.Models;

namespace Apex.Services.Numbering
{
    /// <summary>
    /// Manages dependencies between CycleJobs.
    /// - Tracks statuses.
    /// - Determines if dependencies are logically completed (CompletedPhysical or Skipped).
    /// - Provides dependents resolution for enqueueing next jobs.
    /// </summary>
    public class JobDependencyManager
    {
        private readonly ConcurrentDictionary<string, CycleJob> _jobs = new();
        private readonly ConcurrentDictionary<string, List<string>> _dependents = new(); // parent -> children

        public void AddJob(CycleJob job)
        {
            _jobs[job.JobId] = job;

            if (!string.IsNullOrEmpty(job.DependsOnJobId))
            {
                _dependents.AddOrUpdate(
                    job.DependsOnJobId,
                    _ => new List<string> { job.JobId },
                    (_, list) =>
                    {
                        lock (list) { list.Add(job.JobId); }
                        return list;
                    });
            }
        }

        /// <summary>
        /// Update status and return the job after update.
        /// </summary>
        public CycleJob? UpdateStatus(string jobId, CycleStatus status, string? errorMessage = null)
        {
            if (_jobs.TryGetValue(jobId, out var job))
            {
                job.Status = status;
                job.ErrorMessage = errorMessage;

                if (status == CycleStatus.Printing && job.StartedAtUtc == null)
                    job.StartedAtUtc = DateTime.UtcNow;

                if (status.IsTerminal())
                    job.CompletedAtUtc ??= DateTime.UtcNow;

                return job;
            }

            return null;
        }

        public CycleJob? GetJob(string jobId)
        {
            _jobs.TryGetValue(jobId, out var job);
            return job;
        }

        /// <summary>
        /// Returns true if ALL dependencies are logically completed (CompletedPhysical or Skipped).
        /// </summary>
        public bool AreDependenciesSatisfied(CycleJob job)
        {
            if (string.IsNullOrEmpty(job.DependsOnJobId))
                return true;

            if (!_jobs.TryGetValue(job.DependsOnJobId, out var parent))
                return false;

            return parent.Status.IsCompletedLogical();
        }

        /// <summary>
        /// Returns dependent job IDs for a given job ID.
        /// </summary>
        public IReadOnlyList<string> GetDependents(string jobId)
        {
            if (_dependents.TryGetValue(jobId, out var list))
            {
                lock (list)
                {
                    return list.ToList();
                }
            }

            return Array.Empty<string>();
        }

        /// <summary>
        /// Returns jobs that are ready (dependencies satisfied) and still pending/paused/retrying.
        /// Used by the sequenced queue to enqueue work.
        /// </summary>
        public IEnumerable<CycleJob> GetReadyDependents(string completedJobId)
        {
            foreach (var childId in GetDependents(completedJobId))
            {
                if (_jobs.TryGetValue(childId, out var child))
                {
                    if (AreDependenciesSatisfied(child) &&
                        (child.Status == CycleStatus.Pending || child.Status == CycleStatus.Retrying || child.Status == CycleStatus.Paused))
                    {
                        yield return child;
                    }
                }
            }
        }
    }
}

