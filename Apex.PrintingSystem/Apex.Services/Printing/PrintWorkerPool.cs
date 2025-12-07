using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.Services.Printing
{
    /// <summary>
    /// Manages a pool of worker threads for parallel print job processing.
    /// Prevents thread exhaustion and provides controlled concurrency.
    /// </summary>
    public class PrintWorkerPool : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly int _maxConcurrency;
        private readonly ConcurrentBag<Task> _activeTasks = new();
        private bool _disposed;

        public PrintWorkerPool(int maxConcurrency = 4)
        {
            _maxConcurrency = maxConcurrency;
            _semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        }

        /// <summary>
        /// Gets the number of currently active workers.
        /// </summary>
        public int ActiveWorkers => _maxConcurrency - _semaphore.CurrentCount;

        /// <summary>
        /// Gets the maximum number of concurrent workers.
        /// </summary>
        public int MaxConcurrency => _maxConcurrency;

        /// <summary>
        /// Enqueues work to be executed by the worker pool.
        /// </summary>
        public async Task EnqueueWorkAsync(Func<Task> work, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PrintWorkerPool));

            await _semaphore.WaitAsync(cancellationToken);

            var task = Task.Run(async () =>
            {
                try
                {
                    await work();
                }
                finally
                {
                    _semaphore.Release();
                }
            }, cancellationToken);

            _activeTasks.Add(task);
        }

        /// <summary>
        /// Enqueues work with a result.
        /// </summary>
        public async Task<T> EnqueueWorkAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PrintWorkerPool));

            await _semaphore.WaitAsync(cancellationToken);

            try
            {
                return await work();
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Waits for all active tasks to complete.
        /// </summary>
        public async Task WaitForAllAsync()
        {
            var tasks = _activeTasks.ToArray();
            await Task.WhenAll(tasks);
            _activeTasks.Clear();
        }

        /// <summary>
        /// Disposes the worker pool and releases resources.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            _semaphore?.Dispose();
            _disposed = true;
        }
    }

    /// <summary>
    /// Specialized worker pool for file preparation tasks.
    /// </summary>
    public class FilePreparationWorkerPool : PrintWorkerPool
    {
        public FilePreparationWorkerPool() : base(maxConcurrency: 8)
        {
            // File preparation can be more concurrent as it's CPU-bound
        }
    }

    /// <summary>
    /// Specialized worker pool for printer dispatch tasks.
    /// </summary>
    public class PrinterDispatchWorkerPool : PrintWorkerPool
    {
        public PrinterDispatchWorkerPool() : base(maxConcurrency: 4)
        {
            // Printing should be more conservative to avoid overwhelming printers
        }
    }
}
