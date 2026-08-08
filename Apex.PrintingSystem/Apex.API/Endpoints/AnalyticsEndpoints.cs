using Apex.Services.Printing.Queue;
using Apex.Services.Printing.Resilience;

namespace Apex.API.Endpoints
{
    public static class AnalyticsEndpoints
    {
        public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/analytics").WithTags("Analytics");

            // ── Today's session stats ──────────────────────────────────────
            group.MapGet("/today", () =>
            {
                var stats = PrintJobQueueManager.Instance.GetStatistics();
                var health = CircuitBreakerManager.Instance.GetHealthSnapshot();

                return Results.Ok(new
                {
                    session = new
                    {
                        totalJobsQueued = stats.TotalJobsQueued,
                        totalJobsCompleted = stats.TotalJobsCompleted,
                        totalJobsFailed = stats.TotalJobsFailed,
                        successRate = $"{stats.SuccessRate:F1}%",
                        activeJobs = stats.ActiveJobs,
                        queuedJobs = stats.QueuedJobs,
                        printersWithQueues = stats.PrintersWithQueues
                    },
                    printerHealth = health.Select(kvp => new
                    {
                        printer = kvp.Key,
                        healthScore = kvp.Value,
                        status = CircuitBreakerManager.Instance.GetState(kvp.Key).ToString()
                    }).ToList()
                });
            })
            .WithSummary("Get today's print analytics and printer health");

            // ── System health ──────────────────────────────────────────────
            group.MapGet("/health", () =>
            {
                var stats = PrintJobQueueManager.Instance.GetStatistics();
                bool healthy = stats.SuccessRate >= 80;

                return Results.Ok(new
                {
                    status = healthy ? "healthy" : "degraded",
                    successRate = stats.SuccessRate,
                    activeJobs = stats.ActiveJobs,
                    timestamp = DateTime.UtcNow
                });
            })
            .WithSummary("System health check");

            return app;
        }
    }
}
