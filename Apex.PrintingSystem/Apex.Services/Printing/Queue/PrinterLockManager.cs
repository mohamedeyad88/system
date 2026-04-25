using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.Services.Printing.Queue
{
    /// <summary>
    /// Manages exclusive locks on printers to ensure only one job is active per printer.
    /// CRITICAL: Prevents parallel writes to the same printer which causes corruption and network storms.
    /// 
    /// Design Philosophy:
    /// - Printers are NOT servers - they can only handle one stream at a time
    /// - Locks MUST be released even if job fails
    /// - Lock timeout prevents deadlocks from crashed jobs
    /// </summary>
    public class PrinterLockManager
    {
        private readonly ConcurrentDictionary<string, PrinterLock> _locks = new();
        private readonly TimeSpan _lockTimeout = TimeSpan.FromMinutes(10); // Safety timeout
        
        private static readonly Lazy<PrinterLockManager> _instance = 
            new(() => new PrinterLockManager());
        
        public static PrinterLockManager Instance => _instance.Value;
        
        private PrinterLockManager() { }
        
        /// <summary>
        /// Attempts to acquire a lock on the specified printer.
        /// Returns true if lock was acquired, false if printer is already locked.
        /// </summary>
        public async Task<PrinterLockHandle?> TryAcquireLockAsync(
            string printerName, 
            string jobId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(printerName))
                throw new ArgumentException("Printer name cannot be null or empty", nameof(printerName));
            
            var printerLock = _locks.GetOrAdd(printerName, _ => new PrinterLock());
            
            // Try to acquire the semaphore with timeout
            var acquired = await printerLock.Semaphore.WaitAsync(0, cancellationToken);
            
            if (!acquired)
            {
                Debug.WriteLine($"[PrinterLock] Failed to acquire lock for '{printerName}' - already locked by job {printerLock.CurrentJobId}");
                return null;
            }
            
            // Lock acquired!
            printerLock.CurrentJobId = jobId;
            printerLock.LockedAt = DateTime.UtcNow;
            printerLock.LockCount++;
            
            Debug.WriteLine($"[PrinterLock] ✓ Acquired lock for '{printerName}' by job {jobId} (lock #{printerLock.LockCount})");
            
            return new PrinterLockHandle(printerName, jobId, this);
        }
        
        /// <summary>
        /// Waits until lock becomes available, then acquires it.
        /// Use with caution - can cause long waits if printer is busy.
        /// </summary>
        public async Task<PrinterLockHandle> AcquireLockAsync(
            string printerName,
            string jobId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(printerName))
                throw new ArgumentException("Printer name cannot be null or empty", nameof(printerName));
            
            var printerLock = _locks.GetOrAdd(printerName, _ => new PrinterLock());
            
            Debug.WriteLine($"[PrinterLock] Waiting for lock on '{printerName}' for job {jobId}...");
            
            // Wait for lock with timeout to prevent deadlocks
            var acquired = await printerLock.Semaphore.WaitAsync(_lockTimeout, cancellationToken);
            
            if (!acquired)
                throw new TimeoutException($"Failed to acquire lock on printer '{printerName}' within {_lockTimeout.TotalSeconds}s - possible deadlock");
            
            printerLock.CurrentJobId = jobId;
            printerLock.LockedAt = DateTime.UtcNow;
            printerLock.LockCount++;
            
            Debug.WriteLine($"[PrinterLock] ✓ Acquired lock for '{printerName}' by job {jobId} (lock #{printerLock.LockCount})");
            
            return new PrinterLockHandle(printerName, jobId, this);
        }
        
        /// <summary>
        /// Releases the lock on a printer.
        /// MUST be called when job completes (success or failure).
        /// </summary>
        internal void ReleaseLock(string printerName, string jobId)
        {
            if (!_locks.TryGetValue(printerName, out var printerLock))
            {
                Debug.WriteLine($"[PrinterLock] WARNING: Attempted to release non-existent lock on '{printerName}'");
                return;
            }
            
            if (printerLock.CurrentJobId != jobId)
            {
                Debug.WriteLine($"[PrinterLock] WARNING: Job {jobId} tried to release lock owned by {printerLock.CurrentJobId}");
                // Still release to prevent deadlock
            }
            
            printerLock.CurrentJobId = null;
            printerLock.LockedAt = null;
            printerLock.Semaphore.Release();
            
            Debug.WriteLine($"[PrinterLock] ✓ Released lock on '{printerName}' from job {jobId}");
        }
        
        /// <summary>
        /// Checks if a printer is currently locked.
        /// </summary>
        public bool IsLocked(string printerName)
        {
            if (!_locks.TryGetValue(printerName, out var printerLock))
                return false;
            
            return printerLock.CurrentJobId != null;
        }
        
        /// <summary>
        /// Gets the current job ID holding the lock (if any).
        /// </summary>
        public string? GetCurrentLockHolder(string printerName)
        {
            return _locks.TryGetValue(printerName, out var printerLock) 
                ? printerLock.CurrentJobId 
                : null;
        }
        
        /// <summary>
        /// Forces release of all locks (emergency use only).
        /// </summary>
        public void ReleaseAllLocks()
        {
            Debug.WriteLine("[PrinterLock] ⚠️ Releasing ALL printer locks (emergency)");
            
            foreach (var kvp in _locks)
            {
                var printerLock = kvp.Value;
                if (printerLock.CurrentJobId != null)
                {
                    printerLock.CurrentJobId = null;
                    printerLock.LockedAt = null;
                    
                    // Release all waiting threads
                    while (printerLock.Semaphore.CurrentCount == 0)
                    {
                        try { printerLock.Semaphore.Release(); }
                        catch { break; }
                    }
                }
            }
        }
        
        /// <summary>
        /// Internal lock state for a printer.
        /// </summary>
        private class PrinterLock
        {
            public SemaphoreSlim Semaphore { get; } = new(1, 1);
            public string? CurrentJobId { get; set; }
            public DateTime? LockedAt { get; set; }
            public int LockCount { get; set; }
        }
    }
    
    /// <summary>
    /// Handle for a printer lock that automatically releases when disposed.
    /// MUST be used in a using statement to ensure lock is always released.
    /// </summary>
    public class PrinterLockHandle : IDisposable
    {
        private readonly string _printerName;
        private readonly string _jobId;
        private readonly PrinterLockManager _manager;
        private bool _disposed;
        
        internal PrinterLockHandle(string printerName, string jobId, PrinterLockManager manager)
        {
            _printerName = printerName;
            _jobId = jobId;
            _manager = manager;
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            
            _manager.ReleaseLock(_printerName, _jobId);
            _disposed = true;
            
            GC.SuppressFinalize(this);
        }
        
        ~PrinterLockHandle()
        {
            if (!_disposed)
            {
                Debug.WriteLine($"[PrinterLock] ⚠️ Lock handle for '{_printerName}' was not disposed properly!");
                Dispose();
            }
        }
    }
}
