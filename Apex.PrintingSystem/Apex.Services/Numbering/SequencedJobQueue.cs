using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Apex.Core.Enums;
using Apex.Core.Models;

namespace Apex.Services.Numbering
{
    /// <summary>
    /// Sequenced queue for CycleJobs with strict dependency enforcement.
    /// - No parallel execution (one-at-a-time semantics).
    /// - Uses CompletedLogical (CompletedPhysical or Skipped) for dependency resolution.
    /// - Supports Pause/Resume/Retry/Skip.
    /// </summary>
    public class SequencedJobQueue
    {
        private readonly JobDependencyManager _dependencyManager;
        private readonly ConcurrentQueue<CycleJob> _readyQueue = new();
        private readonly ConcurrentDictionary<string, CycleJob> _all = new();
        private volatile bool _isPaused;

        public SequencedJobQueue(JobDependencyManager dependencyManager)
        {
            _dependencyManager = dependencyManager;
        }

        public bool IsPaused => _isPaused;

        public void Pause() => _isPaused = true;
        public void Resume() => _isPaused = false;

        /// <summary>
        /// Enqueue a job; if dependencies are satisfied, it goes to ready queue.
        /// Otherwise it will be picked up when parent completes.
        /// </summary>
        public void Enqueue(CycleJob job)
        {
            _dependencyManager.AddJob(job);
            _all[job.JobId] = job;

            if (_dependencyManager.AreDependenciesSatisfied(job))
            {
                _readyQueue.Enqueue(job);
            }
        }

        /// <summary>
        /// Try to dequeue the next ready job (returns null if paused or none).
        /// </summary>
        public CycleJob? DequeueReady()
        {
            if (_isPaused)
                return null;

            if (_readyQueue.TryDequeue(out var job))
            {
                // Only allow if still pending/retrying/paused
                if (job.Status == CycleStatus.Pending || job.Status == CycleStatus.Retrying || job.Status == CycleStatus.Paused)
                    return job;
            }
            return null;
        }

        /// <summary>
        /// Update job status and auto-enqueue dependents when logically complete.
        /// </summary>
        public void UpdateJobStatus(string jobId, CycleStatus status, string? errorMessage = null)
        {
            var job = _dependencyManager.UpdateStatus(jobId, status, errorMessage);
            if (job == null) return;

            if (status.IsCompletedLogical())
            {
                foreach (var child in _dependencyManager.GetReadyDependents(jobId))
                {
                    _readyQueue.Enqueue(child);
                }
            }
        }

        /// <summary>
        /// Retry a failed or skipped job (re-queue if dependencies satisfied).
        /// </summary>
        public bool RetryJob(string jobId)
        {
            if (_all.TryGetValue(jobId, out var job))
            {
                job.Status = CycleStatus.Retrying;
                job.ErrorMessage = null;
                if (_dependencyManager.AreDependenciesSatisfied(job))
                {
                    _readyQueue.Enqueue(job);
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Skip a job: mark Skipped (CompletedLogical) and enqueue dependents.
        /// </summary>
        public bool SkipJob(string jobId, string? reason = null)
        {
            var job = _dependencyManager.UpdateStatus(jobId, CycleStatus.Skipped, reason);
            if (job == null) return false;

            foreach (var child in _dependencyManager.GetReadyDependents(jobId))
                _readyQueue.Enqueue(child);

            return true;
        }

        /// <summary>
        /// Get snapshot of queue for UI/monitoring.
        /// </summary>
        public IReadOnlyCollection<CycleJob> GetAllJobs() => _all.Values.ToList();
    }
}

