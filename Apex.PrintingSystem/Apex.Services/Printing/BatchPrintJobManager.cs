using Apex.Core.Interfaces;
using Apex.Core.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.Services.Printing
{
    /// <summary>
    /// Manages batch print jobs with integrated document conversion.
    /// Flow: Pending → Converting → Printing → Completed
    /// </summary>
    public class BatchPrintJobManager
    {
        private readonly IPrintEngine _printEngine;
        private readonly IPrintJobLogger _logger;
        private readonly IDocumentConverter? _converter;
        private readonly IPrinterHealthProbe? _health;

        public event EventHandler<BatchJob>? OnJobStatusChanged;
        public event EventHandler<string>? OnBatchStatusChanged;
        public event EventHandler<BatchProgress>? OnBatchProgressChanged;

        /// <summary>A station stopped taking work and is waiting for someone.</summary>
        public event EventHandler<PrinterHoldEventArgs>? OnPrinterHeld;

        /// <summary>A held station was attended to and took its work.</summary>
        public event EventHandler<string>? OnPrinterResumed;

        private CancellationTokenSource? _cts;
        private bool _isPaused;
        private readonly ManualResetEventSlim _pauseEvent = new(true);

        private int _outstandingCopies;

        /// <summary>
        /// A run is in progress. The whole batch executes inside this process — each
        /// page is rendered here and handed to the spooler one copy at a time — so
        /// anything still owed dies with the process. Closing the app therefore has
        /// to ask first, which is what this flag is for.
        /// </summary>
        public bool IsRunning { get; private set; }

        /// <summary>
        /// Copies the run still owes: never printed, never failed, never held. Zero
        /// while idle. This is what would be lost if the app were closed right now.
        /// </summary>
        public int OutstandingCopies => Volatile.Read(ref _outstandingCopies);

        /// <summary>
        /// Stations the operator gave up on. A held printer otherwise waits forever,
        /// which is correct — the shop's rule is that work is never silently skipped —
        /// but there has to be a way out when a device is broken for the day.
        /// </summary>
        private readonly HashSet<string> _abandoned = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>How often a held printer is re-checked.</summary>
        private static readonly TimeSpan HoldPollInterval = TimeSpan.FromSeconds(2);

        public BatchPrintJobManager(
            IPrintEngine printEngine,
            IPrintJobLogger logger,
            IDocumentConverter? converter = null,
            IPrinterHealthProbe? health = null)
        {
            _printEngine = printEngine;
            _logger = logger;
            _converter = converter;
            _health = health;
        }

        /// <summary>
        /// Stop waiting on a held printer and record its outstanding copies as
        /// skipped. Called from the UI when the operator decides a station is out
        /// of service.
        /// </summary>
        public void AbandonPrinter(string printerName)
        {
            if (!string.IsNullOrWhiteSpace(printerName))
                _abandoned.Add(printerName);
        }

        /// <summary>
        /// The fault a printer is showing, or null if it can take work.
        ///
        /// Warnings are deliberately not faults: a tray that is merely low still
        /// prints, and stopping for it would idle a station that could have
        /// finished the run.
        /// </summary>
        private string? FaultOn(string printerName)
        {
            var status = _health?.GetCurrentStatus(printerName);
            return status is { RequiresIntervention: true } ? status.Status : null;
        }

        /// <summary>
        /// Blocks until the printer can take work again, the operator abandons it,
        /// or the batch is stopped. Returns false if the copy should not be printed.
        /// </summary>
        private async Task<bool> WaitForPrinterAsync(
            string printerName, string fault, CancellationToken ct)
        {
            OnPrinterHeld?.Invoke(this, new PrinterHoldEventArgs(printerName, fault));
            Apex.Services.Logging.PrintLogger.Warning(
                "[Batch] Holding '{Printer}' — {Fault}. Waiting for the operator.",
                printerName, fault);

            string lastFault = fault;

            while (!ct.IsCancellationRequested)
            {
                if (_abandoned.Contains(printerName))
                {
                    Apex.Services.Logging.PrintLogger.Warning(
                        "[Batch] '{Printer}' abandoned by the operator while held.", printerName);
                    return false;
                }

                var current = FaultOn(printerName);
                if (current == null)
                {
                    OnPrinterResumed?.Invoke(this, printerName);
                    Apex.Services.Logging.PrintLogger.Info(
                        "[Batch] '{Printer}' is ready again — resuming.", printerName);
                    return true;
                }

                // The fault can change while we wait — paper refilled but the cover
                // left open. Re-announce so the operator is told what to fix now.
                if (!string.Equals(current, lastFault, StringComparison.Ordinal))
                {
                    lastFault = current;
                    OnPrinterHeld?.Invoke(this, new PrinterHoldEventArgs(printerName, current));
                }

                try { await Task.Delay(HoldPollInterval, ct); }
                catch (OperationCanceledException) { return false; }
            }

            return false;
        }

        /// <summary>
        /// Processes a batch of jobs: converts then prints sequentially to one printer.
        /// </summary>
        /// <summary>Single-printer batch — kept so existing callers are unaffected.</summary>
        public Task<BatchResult> ProcessBatchAsync(string printerName, List<BatchJob> jobs, BatchSettings settings)
            => ProcessBatchAsync(new[] { printerName }, jobs, settings, PrintDistributionMode.LoadBalance);

        /// <summary>
        /// Runs a batch across one or more printers.
        ///
        /// <para><b>Duplicate</b> — every file is sent to EVERY printer. Used when the
        /// same document must come out at several stations (branches, or a spare copy).</para>
        ///
        /// <para><b>LoadBalance</b> — files are dealt round-robin across the printers so
        /// a long queue finishes sooner. Submission stays sequential on purpose:
        /// handing a job to the spooler is fast, and the devices then print in
        /// parallel — so this gets the speed-up without the failure modes of running
        /// several print pipelines at once.</para>
        /// </summary>
        public async Task<BatchResult> ProcessBatchAsync(
            IReadOnlyList<string> printerNames,
            List<BatchJob> jobs,
            BatchSettings settings,
            PrintDistributionMode mode = PrintDistributionMode.LoadBalance)
        {
            if (printerNames == null || printerNames.Count == 0)
                throw new ArgumentException("لا توجد طابعات محددة.", nameof(printerNames));

            var printers = printerNames.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            if (printers.Count == 0)
                throw new ArgumentException("لا توجد طابعات صالحة.", nameof(printerNames));

            return await ProcessBatchCoreAsync(printers, jobs, settings, mode);
        }

        private async Task<BatchResult> ProcessBatchCoreAsync(
            List<string> printers, List<BatchJob> jobs, BatchSettings settings, PrintDistributionMode mode)
        {
            _cts = new CancellationTokenSource();
            _isPaused = false;
            _pauseEvent.Set();

            // StopOnError must halt the run WITHOUT looking like a user cancel: the
            // linked source cancels the workers, while _cts (checked for WasCancelled)
            // stays untouched unless the operator actually pressed Cancel.
            using var runCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            var ct = runCts.Token;

            var batchStopwatch = Stopwatch.StartNew();

            // ── Parallel across printers ──────────────────────────────────────────
            // The batch used to run one file at a time — file 1 fully printed before
            // file 2 began — so eight of nine stations sat idle and a nine-printer wall
            // delivered single-printer throughput. Now every printer runs its own
            // worker in parallel: real load balancing. Several threads therefore touch
            // the tallies below, so all shared state is guarded by tallyLock — taken
            // only when a copy FINISHES, never while a page is printing, so it costs
            // nothing on the hot path.
            //
            // Copies = file × printer: in Duplicate 3 files on 9 printers is 27 copies,
            // which is what the UI header promises; counting whole files hid the copies
            // that a single refusing station left unprinted.
            int completedCount = 0;
            int failedCount = 0;
            int copiesPrinted = 0;
            int copiesFailed = 0;
            int copiesHeld = 0;   // never attempted — station held when the run ended
            var targetFailures = new List<BatchFailure>();
            var heldFailures = new List<BatchFailure>();
            var tallyLock = new object();

            // Jobs finished on an earlier pass count as completed and are not re-sent.
            var pending = new List<BatchJob>();
            foreach (var job in jobs)
            {
                if (job.Status == "Completed" || job.Status == "Skipped") completedCount++;
                else pending.Add(job);
            }
            ReportBatchProgress(jobs, completedCount, failedCount, batchStopwatch.Elapsed);

            // Convert each file at most once, even when every printer needs it
            // (Duplicate). Lazy guarantees the conversion body runs a single time no
            // matter how many workers ask for the same file at once.
            var conversions = new System.Collections.Concurrent.ConcurrentDictionary<BatchJob, Lazy<Task<bool>>>();
            Task<bool> EnsureConvertedAsync(BatchJob job)
                => conversions.GetOrAdd(job, j => new Lazy<Task<bool>>(() => ConvertJobAsync(j, ct))).Value;

            // Copies each pending job owes: one per printer in Duplicate, exactly one
            // (whichever printer picks it up) in LoadBalance. A job is finalised —
            // counted completed or failed — only when its last owed copy is done, so a
            // job counts as succeeded only if EVERY copy it owed actually printed.
            int copiesPerJob = mode == PrintDistributionMode.Duplicate ? Math.Max(1, printers.Count) : 1;
            var remaining = new Dictionary<BatchJob, int>();
            // A held copy is a THIRD outcome, not a failure: it must not mark the job
            // "Failed" nor land in the Failures list — the shop rule is that a waiting
            // copy sends the operator to close a cover, not to hunt a fault.
            var hardFailed = new Dictionary<BatchJob, bool>();
            var held = new Dictionary<BatchJob, bool>();
            foreach (var job in pending) { remaining[job] = copiesPerJob; hardFailed[job] = false; held[job] = false; }

            // What the run still owes, so the shell can say what closing would throw away.
            Volatile.Write(ref _outstandingCopies, pending.Count * copiesPerJob);
            IsRunning = true;

            // Record one (file, printer) copy: 'P' printed, 'F' failed, 'H' held. When
            // it is the file's last owed copy, finalise the job and report progress.
            void RecordCopy(BatchJob job, string printer, char outcome, string? fault)
            {
                lock (tallyLock)
                {
                    // Settled one way or another — printed, failed or held — so it is no
                    // longer work that closing the app would silently discard.
                    Interlocked.Decrement(ref _outstandingCopies);

                    if (outcome == 'P')
                    {
                        copiesPrinted++;
                    }
                    else if (outcome == 'F')
                    {
                        copiesFailed++;
                        hardFailed[job] = true;
                        targetFailures.Add(new BatchFailure(
                            System.IO.Path.GetFileName(job.FilePath),
                            string.IsNullOrWhiteSpace(job.ErrorMessage) ? "سبب غير معروف" : job.ErrorMessage!,
                            printer));
                    }
                    else // 'H'
                    {
                        copiesHeld++;
                        held[job] = true;
                        heldFailures.Add(new BatchFailure(
                            System.IO.Path.GetFileName(job.FilePath), fault ?? "معلّق", printer));
                    }

                    if (--remaining[job] == 0)
                    {
                        // A real failure wins over a hold; only an all-printed job is
                        // Completed. A job whose sole snag was a held copy is counted a
                        // failure (it did not finish) but is NOT stamped "Failed" — that
                        // status is what leaks a held copy into the Failures list.
                        if (hardFailed[job]) { failedCount++; job.Status = "Failed"; }
                        else if (held[job]) { failedCount++; }
                        else { completedCount++; job.Status = "Completed"; }
                        NotifyJobStatus(job);
                        ReportBatchProgress(jobs, completedCount, failedCount, batchStopwatch.Elapsed);
                    }
                }
            }

            // Print one copy on one printer: convert once, honour a held station (wait
            // for THIS printer, never skip or reroute — the shop's rule), then print
            // with retry. Records exactly one copy outcome.
            async Task PrintCopyAsync(BatchJob job, string printer)
            {
                try
                {
                    if (!await EnsureConvertedAsync(job))
                    {
                        RecordCopy(job, printer, 'F', null);
                        if (settings.StopOnError) { OnBatchStatusChanged?.Invoke(this, "Batch stopped on error"); runCts.Cancel(); }
                        return;
                    }

                    var fault = FaultOn(printer);
                    if (fault != null)
                    {
                        OnBatchStatusChanged?.Invoke(this, $"بانتظار «{printer}» — {fault}");
                        if (!await WaitForPrinterAsync(printer, fault, ct))
                        {
                            RecordCopy(job, printer, 'H', fault);
                            return;
                        }
                    }

                    bool ok = await PrintJobWithRetryAsync(printer, job, settings, ct);
                    RecordCopy(job, printer, ok ? 'P' : 'F', null);

                    if (!ok && settings.StopOnError)
                    {
                        OnBatchStatusChanged?.Invoke(this, "Batch stopped on error");
                        runCts.Cancel();
                        return;
                    }

                    if (settings.DelayBetweenJobsMs > 0)
                        await Task.Delay(settings.DelayBetweenJobsMs, ct);
                }
                catch (OperationCanceledException)
                {
                    // Cancelled or stopped mid-copy: not a failure and not a hold — just
                    // stop. The job stays unfinalised and BatchResult.WasCancelled (user
                    // cancel only) carries the outcome.
                }
                catch (Exception ex)
                {
                    job.ErrorMessage = ex.Message;
                    RecordCopy(job, printer, 'F', null);
                }
            }

            OnBatchStatusChanged?.Invoke(this, "Starting batch...");

            // LoadBalance: all printers pull from one shared queue, so a fast station
            // simply takes more files. (Unused in Duplicate, where each worker prints
            // the whole list to its own station.)
            var sharedQueue = new System.Collections.Concurrent.ConcurrentQueue<BatchJob>(pending);

            async Task RunPrinterAsync(string printer)
            {
                if (mode == PrintDistributionMode.Duplicate)
                {
                    foreach (var job in pending)
                    {
                        if (ct.IsCancellationRequested) break;
                        try { _pauseEvent.Wait(ct); } catch (OperationCanceledException) { break; }
                        await PrintCopyAsync(job, printer);
                    }
                    return;
                }

                while (!ct.IsCancellationRequested)
                {
                    try { _pauseEvent.Wait(ct); } catch (OperationCanceledException) { break; }
                    if (!sharedQueue.TryDequeue(out var job)) break;
                    await PrintCopyAsync(job, printer);
                }
            }

            // One worker per printer, all running at once.
            try
            {
                await Task.WhenAll(printers.Select(p => Task.Run(() => RunPrinterAsync(p))));
            }
            finally
            {
                IsRunning = false;
                Volatile.Write(ref _outstandingCopies, 0);
            }

            batchStopwatch.Stop();
            var finalStatus = _cts.IsCancellationRequested
                ? "Batch cancelled"
                : $"Batch completed: {completedCount} succeeded, {failedCount} failed";

            OnBatchStatusChanged?.Invoke(this, finalStatus);

            // Hand the outcome back to the caller instead of only announcing it on an
            // event. The counts were computed here and then discarded: the UI showed a
            // bare "done" dialog whether every job printed or every job failed, so a
            // failed run was indistinguishable from a successful one.
            return new BatchResult
            {
                TotalJobs = jobs.Count,
                Succeeded = completedCount,
                Failed = failedCount,
                CopiesPrinted = copiesPrinted,
                CopiesFailed = copiesFailed,
                CopiesHeld = copiesHeld,
                HeldCopies = heldFailures,
                WasCancelled = _cts.IsCancellationRequested,
                Elapsed = batchStopwatch.Elapsed,

                // Per (file, printer). Jobs that failed before reaching a printer at
                // all — conversion, or an exception — are added here too so nothing
                // fails silently.
                Failures = targetFailures
                    .Concat(jobs
                        .Where(j => j.Status == "Failed"
                                    && !targetFailures.Any(f => f.FileName == System.IO.Path.GetFileName(j.FilePath)))
                        .Select(j => new BatchFailure(
                            System.IO.Path.GetFileName(j.FilePath),
                            string.IsNullOrWhiteSpace(j.ErrorMessage) ? "سبب غير معروف" : j.ErrorMessage!,
                            Printer: null)))
                    .ToList(),
            };
        }

        /// <summary>
        /// Which printers this job goes to.
        /// Duplicate → all of them; LoadBalance → the next one in rotation.
        /// </summary>
        public static IReadOnlyList<string> TargetsFor(
            IReadOnlyList<string> printers, PrintDistributionMode mode, int jobIndex)
        {
            if (printers.Count == 0) return Array.Empty<string>();
            if (mode == PrintDistributionMode.Duplicate) return printers;

            int i = jobIndex < 0 ? 0 : jobIndex % printers.Count;
            return new[] { printers[i] };
        }

        /// <summary>
        /// How many copies this file gets. A count set on the job itself is a
        /// deliberate per-file choice and wins; anything unset (0 or nonsense)
        /// falls back to the batch default, never to zero copies.
        /// </summary>
        public static int CopiesFor(Apex.Core.Models.BatchJob job, Apex.Core.Models.BatchSettings settings)
        {
            if (job.Copies > 0) return job.Copies;
            return settings.Copies > 0 ? settings.Copies : 1;
        }

        /// <summary>
        /// The settings this file is actually printed with. Everything the operator
        /// chose — duplex, colour, orientation, page range — has to travel with the
        /// job; the batch used to submit copies alone, so every other control on the
        /// settings bar was silently ignored by the driver.
        /// </summary>
        public static Apex.Core.Interfaces.PrintJobSettings SettingsFor(
            Apex.Core.Models.BatchJob job, Apex.Core.Models.BatchSettings settings)
        {
            var range = !string.IsNullOrWhiteSpace(job.PageRange)
                ? job.PageRange
                : settings.PageRange;

            return new Apex.Core.Interfaces.PrintJobSettings
            {
                Copies = CopiesFor(job, settings),
                Duplex = settings.Duplex,
                Color = settings.ColorMode,
                PageRange = string.IsNullOrWhiteSpace(range) ? "All" : range,
                PaperSize = settings.PaperSize,
                Orientation = settings.Orientation,
                Quality = settings.Quality,
            };
        }

        /// <summary>
        /// Converts a single job to PDF if needed.
        /// </summary>
        private async Task<bool> ConvertJobAsync(BatchJob job, CancellationToken ct)
        {
            // PDF files skip conversion
            if (job.OriginalExtension == ".pdf")
            {
                job.ConversionStatus = ConversionStatus.Skipped;
                job.ConvertedFilePath = job.FilePath;
                return true;
            }

            if (_converter == null)
            {
                job.ConversionStatus = ConversionStatus.Failed;
                job.ConversionError = "No converter available";
                job.Status = "Failed";
                job.ErrorMessage = "Document conversion not available. Please install LibreOffice.";
                NotifyJobStatus(job);
                return false;
            }

            job.ConversionStatus = ConversionStatus.Converting;
            job.Status = "Converting";
            NotifyJobStatus(job);

            try
            {
                var result = await _converter.ConvertToPdfAsync(job.FilePath, null, ct);

                if (result.Success)
                {
                    job.ConversionStatus = ConversionStatus.Ready;
                    job.ConvertedFilePath = result.OutputPath;
                    job.TotalPages = result.EstimatedPages;
                    return true;
                }
                else
                {
                    job.ConversionStatus = ConversionStatus.Failed;
                    job.ConversionError = result.ErrorMessage;
                    job.Status = "Failed";
                    job.ErrorMessage = $"Conversion failed: {result.ErrorMessage}";
                    NotifyJobStatus(job);
                    return false;
                }
            }
            catch (Exception ex)
            {
                job.ConversionStatus = ConversionStatus.Failed;
                job.ConversionError = ex.Message;
                job.Status = "Failed";
                job.ErrorMessage = $"Conversion error: {ex.Message}";
                NotifyJobStatus(job);
                return false;
            }
        }

        /// <summary>
        /// Prints a job with retry logic and progress tracking.
        /// </summary>
        private async Task<bool> PrintJobWithRetryAsync(
            string printerName, BatchJob job, BatchSettings settings, CancellationToken ct)
        {
            job.Status = "Printing";
            job.StartTime = DateTime.Now;
            job.CurrentPage = 0;
            NotifyJobStatus(job);

            bool success = false;
            int attempts = 0;
            var stopwatch = Stopwatch.StartNew();

            while (!success && attempts <= settings.RetryCount)
            {
                ct.ThrowIfCancellationRequested();
                _pauseEvent.Wait(ct);

                if (attempts > 0)
                {
                    job.Status = $"Retrying ({attempts}/{settings.RetryCount})...";
                    NotifyJobStatus(job);
                    await Task.Delay(1000 * attempts, ct);
                }

                try
                {
                    // Use converted file path if available
                    var fileToPrint = job.PrintFilePath;

                    // Create progress callback for page-by-page updates
                    var pageProgress = new Progress<int>(page =>
                    {
                        job.CurrentPage = page;

                        // Calculate speed and ETA
                        var elapsed = stopwatch.Elapsed.TotalSeconds;
                        if (elapsed > 0 && page > 0)
                        {
                            job.PagesPerSecond = page / elapsed;
                            var remainingPages = job.TotalPages - page;
                            if (job.PagesPerSecond > 0)
                            {
                                var etaSeconds = remainingPages / job.PagesPerSecond;
                                job.ETA = FormatEta(TimeSpan.FromSeconds(etaSeconds));
                            }
                        }

                        NotifyJobStatus(job);
                    });

                    // ═══════════════════════════════════════════════════════════════════
                    // MANDATORY: All printing through SmartPrintManager
                    // Direct printing to _printEngine is DEPRECATED.
                    // ═══════════════════════════════════════════════════════════════════
                    var smartResult = await SmartPrintManager.Instance.SubmitAsync(
                        new SmartPrintRequest
                        {
                            FilePath = fileToPrint,
                            PrinterNames = new List<string> { printerName },
                            Copies = CopiesFor(job, settings),
                            Settings = SettingsFor(job, settings)
                        },
                        ct);

                    success = smartResult.Success;

                    if (!success)
                    {
                        job.ErrorMessage = smartResult.ErrorMessage ?? "Print engine returned failure.";
                    }
                }
                catch (Exception ex)
                {
                    job.ErrorMessage = ex.Message;
                }

                if (!success)
                {
                    attempts++;
                    job.RetryCount = attempts;
                }
            }

            stopwatch.Stop();
            job.TimeTaken = stopwatch.Elapsed;
            job.EndTime = DateTime.Now;
            job.Status = success ? "Completed" : "Failed";
            job.ETA = "";

            _logger.LogJob(printerName, job.FilePath, success, success ? "Success" : job.ErrorMessage);
            NotifyJobStatus(job);

            return success;
        }

        public void Pause()
        {
            _isPaused = true;
            _pauseEvent.Reset();
            OnBatchStatusChanged?.Invoke(this, "Paused");
        }

        public void Resume()
        {
            _isPaused = false;
            _pauseEvent.Set();
            OnBatchStatusChanged?.Invoke(this, "Resumed");
        }

        public void CancelBatch()
        {
            _cts?.Cancel();
            _pauseEvent.Set(); // Unblock if paused
        }

        public bool IsPaused => _isPaused;

        private void NotifyJobStatus(BatchJob job)
        {
            OnJobStatusChanged?.Invoke(this, job);
        }

        /// <summary>
        /// Publishes batch progress.
        ///
        /// PercentComplete counts everything ATTEMPTED, not just what succeeded. It
        /// used to divide by successes alone, so a run where the jobs failed sat at 0%
        /// from beginning to end — the operator watched a motionless bar and had no
        /// way to tell a stuck queue from a failing one.
        /// </summary>
        private void ReportBatchProgress(List<BatchJob> jobs, int completed, int failed, TimeSpan elapsed)
        {
            int attempted = completed + failed;

            var progress = new BatchProgress
            {
                TotalJobs = jobs.Count,
                CompletedJobs = completed,
                FailedJobs = failed,
                AttemptedJobs = attempted,
                PendingJobs = jobs.Count - completed - failed,
                ConvertingJobs = jobs.Count(j => j.ConversionStatus == ConversionStatus.Converting),
                PrintingJobs = jobs.Count(j => j.Status == "Printing"),
                ElapsedTime = elapsed,
                PercentComplete = jobs.Count > 0 ? (double)attempted / jobs.Count * 100 : 0
            };

            OnBatchProgressChanged?.Invoke(this, progress);
        }

        private static string FormatEta(TimeSpan eta)
        {
            if (eta.TotalMinutes >= 1)
                return $"{(int)eta.TotalMinutes}m {eta.Seconds}s";
            return $"{eta.Seconds}s";
        }
    }

    /// <summary>
    /// One (file, printer) pair that did not print, and why.
    /// </summary>
    /// <param name="Printer">
    /// The device that refused it, or null when the job failed before any printer was
    /// reached. Naming the printer is the difference between "three files failed" and
    /// "these three failed on OneNote, which cannot print silently" — only the second
    /// tells the operator what to change.
    /// </param>
    public sealed record BatchFailure(string FileName, string Reason, string? Printer = null);

    /// <summary>
    /// A station stopped taking work and is waiting for someone to attend to it.
    /// Raised the moment the condition is seen, because the operator is usually
    /// standing at a machine rather than watching the screen.
    /// </summary>
    public sealed class PrinterHoldEventArgs : EventArgs
    {
        public string PrinterName { get; }

        /// <summary>What has to be fixed — "Out of Paper", "Door Open", "Paper Jam".</summary>
        public string Fault { get; }

        public PrinterHoldEventArgs(string printerName, string fault)
        {
            PrinterName = printerName;
            Fault = fault;
        }
    }

    /// <summary>
    /// What a batch actually did.
    ///
    /// Returned rather than only announced on an event, because the caller has to be
    /// able to tell a run where everything printed from one where nothing did — and
    /// to name the files that failed. A print shop cannot act on "done".
    /// </summary>
    public sealed class BatchResult
    {
        public int TotalJobs { get; init; }
        public int Succeeded { get; init; }
        public int Failed { get; init; }

        /// <summary>
        /// Copies that reached a printer, counted as file × printer — the same unit the
        /// UI header shows ("3 files · 9 printers · 27 jobs"). A file counts as failed
        /// the moment ONE of its targets refuses it, so the file count alone says
        /// nothing about how much actually came out.
        /// </summary>
        public int CopiesPrinted { get; init; }

        public int CopiesFailed { get; init; }

        /// <summary>
        /// Copies never attempted because their station was waiting for a person when
        /// the run stopped — an empty tray, an open cover, a printer the operator
        /// took out of service.
        ///
        /// Held is a third outcome, not a kind of failure. Nothing went wrong with
        /// these copies; they simply have not been printed yet. Folding them into
        /// the failure count would send the operator looking for a fault instead of
        /// closing a cover.
        /// </summary>
        public int CopiesHeld { get; init; }

        /// <summary>The (file, printer, reason) behind each held copy.</summary>
        public IReadOnlyList<BatchFailure> HeldCopies { get; init; } = Array.Empty<BatchFailure>();

        public int CopiesTotal => CopiesPrinted + CopiesFailed + CopiesHeld;

        public bool WasCancelled { get; init; }
        public TimeSpan Elapsed { get; init; }
        public IReadOnlyList<BatchFailure> Failures { get; init; } = Array.Empty<BatchFailure>();

        public bool AllSucceeded =>
            Failed == 0 && CopiesHeld == 0 && !WasCancelled && Succeeded == TotalJobs;

        /// <summary>Work is outstanding because a station is waiting for attention.</summary>
        public bool HasHeldCopies => CopiesHeld > 0;

        /// <summary>
        /// True only when NO copy came out anywhere. Judged on copies, not files:
        /// eight stations holding their copies is not "nothing printed", however many
        /// files a ninth printer refused.
        /// </summary>
        public bool NothingPrinted => CopiesPrinted == 0 && CopiesTotal > 0;

        /// <summary>Some copies printed and some did not.</summary>
        public bool PartiallyPrinted => CopiesPrinted > 0 && CopiesFailed > 0;
    }

    /// <summary>
    /// Overall batch progress information.
    /// </summary>
    public class BatchProgress
    {
        public int TotalJobs { get; set; }
        public int CompletedJobs { get; set; }
        public int FailedJobs { get; set; }

        /// <summary>Jobs finished either way — what the progress bar tracks.</summary>
        public int AttemptedJobs { get; set; }

        public int PendingJobs { get; set; }
        public int ConvertingJobs { get; set; }
        public int PrintingJobs { get; set; }
        public TimeSpan ElapsedTime { get; set; }
        public double PercentComplete { get; set; }
    }
}
