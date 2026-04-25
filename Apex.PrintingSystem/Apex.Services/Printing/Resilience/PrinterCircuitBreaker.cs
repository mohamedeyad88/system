using System;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Apex.Services.Printing.Resilience
{
    /// <summary>
    /// Circuit breaker state.
    /// </summary>
    public enum CircuitState
    {
        /// <summary>Normal operation — jobs pass through.</summary>
        Closed,
        /// <summary>Printer is considered failed — jobs are blocked.</summary>
        Open,
        /// <summary>Testing recovery — one job allowed through.</summary>
        HalfOpen
    }

    /// <summary>
    /// Event args for circuit state changes.
    /// </summary>
    public class CircuitStateChangedEventArgs : EventArgs
    {
        public string PrinterName { get; }
        public CircuitState OldState { get; }
        public CircuitState NewState { get; }
        public int ConsecutiveFailures { get; }
        public DateTime ChangedAt { get; } = DateTime.UtcNow;

        public CircuitStateChangedEventArgs(string printerName, CircuitState oldState, CircuitState newState, int failures)
        {
            PrinterName = printerName;
            OldState = oldState;
            NewState = newState;
            ConsecutiveFailures = failures;
        }
    }

    /// <summary>
    /// 🔌 PER-PRINTER CIRCUIT BREAKER
    ///
    /// Protects the print queue from hammering a failing printer.
    ///
    /// State machine:
    ///   Closed  ──(N consecutive failures)──► Open
    ///   Open    ──(timeout elapsed)         ──► HalfOpen
    ///   HalfOpen──(success)                 ──► Closed
    ///   HalfOpen──(failure)                 ──► Open
    ///
    /// Typical usage in QueuedPrintExecutor:
    ///   if (!CircuitBreakerManager.Instance.CanExecute(printerName)) { skip job }
    ///   try { print(); CircuitBreakerManager.Instance.RecordSuccess(printerName); }
    ///   catch { CircuitBreakerManager.Instance.RecordFailure(printerName); throw; }
    /// </summary>
    public sealed class PrinterCircuitBreaker
    {
        private readonly object _lock = new();
        private CircuitState _state = CircuitState.Closed;
        private int _consecutiveFailures;
        private DateTime _openedAt = DateTime.MinValue;
        private bool _halfOpenTestInProgress;

        // ── Configuration ─────────────────────────────────────────────────────
        /// <summary>Number of consecutive failures before opening the circuit.</summary>
        public int FailureThreshold { get; }

        /// <summary>How long the circuit stays Open before moving to HalfOpen.</summary>
        public TimeSpan ResetTimeout { get; }

        // ── Identity ──────────────────────────────────────────────────────────
        public string PrinterName { get; }

        // ── Observable state ─────────────────────────────────────────────────
        public CircuitState State { get { lock (_lock) return _state; } }
        public int ConsecutiveFailures { get { lock (_lock) return _consecutiveFailures; } }
        public DateTime? OpenedAt { get { lock (_lock) return _state == CircuitState.Closed ? null : _openedAt; } }

        /// <summary>
        /// Health score 0–100. 100 = perfect, 0 = tripped circuit.
        /// </summary>
        public int HealthScore
        {
            get
            {
                lock (_lock)
                {
                    if (_state == CircuitState.Open) return 0;
                    if (_state == CircuitState.HalfOpen) return 30;
                    // Closed: degrade score proportionally to recent failures
                    if (_consecutiveFailures == 0) return 100;
                    int penalty = (int)((double)_consecutiveFailures / FailureThreshold * 70);
                    return Math.Max(10, 100 - penalty);
                }
            }
        }

        // ── Events ────────────────────────────────────────────────────────────
        public event EventHandler<CircuitStateChangedEventArgs>? StateChanged;

        // ── Constructor ───────────────────────────────────────────────────────
        public PrinterCircuitBreaker(string printerName, int failureThreshold = 3, int resetTimeoutSeconds = 120)
        {
            PrinterName = printerName;
            FailureThreshold = failureThreshold;
            ResetTimeout = TimeSpan.FromSeconds(resetTimeoutSeconds);
        }

        // ── API ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns true if a job should be allowed through to this printer.
        /// Automatically transitions Open → HalfOpen when timeout elapses.
        /// </summary>
        public bool CanExecute()
        {
            lock (_lock)
            {
                switch (_state)
                {
                    case CircuitState.Closed:
                        return true;

                    case CircuitState.Open:
                        if (DateTime.UtcNow - _openedAt >= ResetTimeout)
                        {
                            // Allow one test job through
                            Transition(CircuitState.HalfOpen);
                            _halfOpenTestInProgress = true;
                            return true;
                        }
                        return false;

                    case CircuitState.HalfOpen:
                        // Only one test at a time
                        if (_halfOpenTestInProgress) return false;
                        _halfOpenTestInProgress = true;
                        return true;

                    default:
                        return false;
                }
            }
        }

        /// <summary>
        /// Call after a successful print job.
        /// </summary>
        public void RecordSuccess()
        {
            lock (_lock)
            {
                _consecutiveFailures = 0;
                _halfOpenTestInProgress = false;

                if (_state == CircuitState.HalfOpen)
                {
                    Debug.WriteLine($"[CircuitBreaker] ✅ '{PrinterName}' recovered — closing circuit");
                    Transition(CircuitState.Closed);
                }
            }
        }

        /// <summary>
        /// Call after a failed print job.
        /// </summary>
        public void RecordFailure(string? reason = null)
        {
            lock (_lock)
            {
                _consecutiveFailures++;
                _halfOpenTestInProgress = false;

                bool shouldOpen = _state == CircuitState.HalfOpen
                               || (_state == CircuitState.Closed && _consecutiveFailures >= FailureThreshold);

                if (shouldOpen)
                {
                    _openedAt = DateTime.UtcNow;
                    Debug.WriteLine($"[CircuitBreaker] ⛔ '{PrinterName}' tripped — opening circuit. " +
                                    $"Failures: {_consecutiveFailures}. Reason: {reason ?? "unknown"}. " +
                                    $"Will retry at {_openedAt + ResetTimeout:HH:mm:ss}");
                    Transition(CircuitState.Open);
                }
                else
                {
                    Debug.WriteLine($"[CircuitBreaker] ⚠️ '{PrinterName}' failure {_consecutiveFailures}/{FailureThreshold}. Reason: {reason ?? "unknown"}");
                }
            }
        }

        /// <summary>
        /// Manually reset the circuit breaker (e.g., admin forced reset).
        /// </summary>
        public void Reset()
        {
            lock (_lock)
            {
                _consecutiveFailures = 0;
                _halfOpenTestInProgress = false;
                if (_state != CircuitState.Closed)
                    Transition(CircuitState.Closed);
                Debug.WriteLine($"[CircuitBreaker] 🔄 '{PrinterName}' manually reset");
            }
        }

        // ── Internal ──────────────────────────────────────────────────────────
        private void Transition(CircuitState newState)
        {
            var old = _state;
            _state = newState;

            // Fire event outside lock if possible — use local copy
            var args = new CircuitStateChangedEventArgs(PrinterName, old, newState, _consecutiveFailures);
            System.Threading.ThreadPool.QueueUserWorkItem(_ => StateChanged?.Invoke(this, args));
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // CIRCUIT BREAKER MANAGER — singleton registry for all printers
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 🏭 CIRCUIT BREAKER MANAGER
    ///
    /// Central registry of per-printer circuit breakers.
    /// Use this singleton from QueuedPrintExecutor and anywhere
    /// that submits print jobs.
    ///
    /// Thread-safe. Creates breakers on demand.
    /// </summary>
    public sealed class CircuitBreakerManager
    {
        private readonly ConcurrentDictionary<string, PrinterCircuitBreaker> _breakers = new();

        // ── Singleton ─────────────────────────────────────────────────────────
        private static readonly Lazy<CircuitBreakerManager> _instance =
            new(() => new CircuitBreakerManager());

        public static CircuitBreakerManager Instance => _instance.Value;

        private CircuitBreakerManager() { }

        // ── Events ────────────────────────────────────────────────────────────

        /// <summary>Fired whenever any printer circuit state changes.</summary>
        public event EventHandler<CircuitStateChangedEventArgs>? AnyCircuitStateChanged;

        // ── API ───────────────────────────────────────────────────────────────

        /// <summary>Get or create the circuit breaker for a printer.</summary>
        public PrinterCircuitBreaker GetOrCreate(string printerName)
        {
            return _breakers.GetOrAdd(printerName, name =>
            {
                var cb = new PrinterCircuitBreaker(name);
                cb.StateChanged += (s, e) => AnyCircuitStateChanged?.Invoke(this, e);
                return cb;
            });
        }

        /// <summary>Returns true if a job may be sent to this printer.</summary>
        public bool CanExecute(string printerName)
            => GetOrCreate(printerName).CanExecute();

        /// <summary>Record a successful print on a printer.</summary>
        public void RecordSuccess(string printerName)
            => GetOrCreate(printerName).RecordSuccess();

        /// <summary>Record a print failure on a printer.</summary>
        public void RecordFailure(string printerName, string? reason = null)
            => GetOrCreate(printerName).RecordFailure(reason);

        /// <summary>Get health score 0–100 for a printer.</summary>
        public int GetHealthScore(string printerName)
            => _breakers.TryGetValue(printerName, out var cb) ? cb.HealthScore : 100;

        /// <summary>Get circuit state for a printer.</summary>
        public CircuitState GetState(string printerName)
            => _breakers.TryGetValue(printerName, out var cb) ? cb.State : CircuitState.Closed;

        /// <summary>Manually reset a printer's circuit breaker.</summary>
        public void Reset(string printerName)
            => GetOrCreate(printerName).Reset();

        /// <summary>Enumerate all tracked circuit breakers.</summary>
        public IEnumerable<PrinterCircuitBreaker> GetAll()
            => _breakers.Values;

        /// <summary>Get a snapshot of all printer health scores.</summary>
        public Dictionary<string, int> GetHealthSnapshot()
        {
            var snapshot = new Dictionary<string, int>();
            foreach (var kvp in _breakers)
                snapshot[kvp.Key] = kvp.Value.HealthScore;
            return snapshot;
        }
    }
}
