using Apex.Services.Printing.Queue;
using Apex.Services.Printing;
using Microsoft.AspNetCore.Mvc;

namespace Apex.API.Endpoints
{
    public record SubmitJobRequest(
        string FilePath,
        string PrinterName,
        int Copies = 1,
        bool DocumentMode = false,
        int Priority = 5
    );

    public record JobStatusResponse(
        string JobId,
        string Status,
        int Progress,
        string PrinterName,
        string FileName,
        string? Error
    );

    public static class PrintEndpoints
    {
        public static IEndpointRouteBuilder MapPrintEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/print").WithTags("Print");

            // ── Submit a print job ─────────────────────────────────────────
            group.MapPost("/submit", async ([FromBody] SubmitJobRequest req) =>
            {
                try
                {
                    if (!File.Exists(req.FilePath))
                        return Results.BadRequest(new { error = $"File not found: {req.FilePath}" });

                    // Run preflight
                    var preflight = await PreflightAnalysisService.Instance.AnalyzeAsync(
                        req.FilePath, req.PrinterName);

                    if (!preflight.CanPrint)
                    {
                        return Results.UnprocessableEntity(new
                        {
                            error = preflight.SummaryArabic,
                            issues = preflight.Items.Select(i => new { i.Severity, i.Category, i.Message })
                        });
                    }

                    var jobId = await PrintJobQueueManager.Instance.SubmitPrintJobAsync(
                        req.PrinterName,
                        req.FilePath,
                        copies: req.Copies,
                        priority: req.Priority,
                        documentMode: req.DocumentMode);

                    return Results.Ok(new
                    {
                        jobId,
                        message = "تم إرسال الوظيفة للطابور",
                        preflight = new
                        {
                            canPrint = preflight.CanPrint,
                            summary = preflight.SummaryArabic,
                            warnings = preflight.WarningCount
                        }
                    });
                }
                catch (Exception ex)
                {
                    return Results.Problem(ex.Message);
                }
            })
            .WithSummary("Submit a print job")
            .WithDescription("Validates the file (pre-flight) and submits it to the print queue.");

            // ── List all jobs ──────────────────────────────────────────────
            group.MapGet("/jobs", () =>
            {
                var jobs = PrintJobQueueManager.Instance.GetAllJobs()
                    .Select(j => new JobStatusResponse(
                        j.JobId,
                        j.State.ToString(),
                        j.Progress,
                        j.PrinterName,
                        Path.GetFileName(j.FilePath),
                        j.ErrorMessage));

                return Results.Ok(jobs);
            })
            .WithSummary("Get all print jobs");

            // ── Get job status ─────────────────────────────────────────────
            group.MapGet("/jobs/{jobId}", (string jobId) =>
            {
                var job = PrintJobQueueManager.Instance.GetJob(jobId);
                if (job == null)
                    return Results.NotFound(new { error = $"Job '{jobId}' not found" });

                return Results.Ok(new JobStatusResponse(
                    job.JobId,
                    job.State.ToString(),
                    job.Progress,
                    job.PrinterName,
                    Path.GetFileName(job.FilePath),
                    job.ErrorMessage));
            })
            .WithSummary("Get job status by ID");

            // ── Cancel a job ───────────────────────────────────────────────
            group.MapDelete("/jobs/{jobId}", (string jobId) =>
            {
                bool ok = PrintJobQueueManager.Instance.CancelJob(jobId);
                return ok
                    ? Results.Ok(new { message = "تم إلغاء الوظيفة" })
                    : Results.NotFound(new { error = "الوظيفة غير موجودة أو مكتملة بالفعل" });
            })
            .WithSummary("Cancel a queued job");

            // ── Queue statistics ───────────────────────────────────────────
            group.MapGet("/stats", () =>
            {
                var stats = PrintJobQueueManager.Instance.GetStatistics();
                return Results.Ok(new
                {
                    stats.QueuedJobs,
                    stats.ActiveJobs,
                    stats.TotalJobsQueued,
                    stats.TotalJobsCompleted,
                    stats.TotalJobsFailed,
                    stats.SuccessRate,
                    stats.PrintersWithQueues
                });
            })
            .WithSummary("Get queue statistics");

            // ── Pre-flight only (dry run) ──────────────────────────────────
            group.MapPost("/preflight", async ([FromBody] SubmitJobRequest req) =>
            {
                var report = await PreflightAnalysisService.Instance.AnalyzeAsync(
                    req.FilePath, req.PrinterName);

                return Results.Ok(new
                {
                    canPrint = report.CanPrint,
                    summary = report.SummaryArabic,
                    severity = report.OverallSeverity.ToString(),
                    errorCount = report.ErrorCount,
                    warningCount = report.WarningCount,
                    durationMs = (int)report.AnalysisDuration.TotalMilliseconds,
                    items = report.Items.Select(i => new
                    {
                        severity = i.Severity.ToString(),
                        category = i.Category,
                        message = i.Message,
                        suggestion = i.Suggestion,
                        blocksPrint = i.BlocksPrint
                    })
                });
            })
            .WithSummary("Validate a file without printing (dry run)");

            return app;
        }
    }
}
