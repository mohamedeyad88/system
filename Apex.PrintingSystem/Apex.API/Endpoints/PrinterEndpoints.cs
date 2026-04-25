using Apex.Core.Interfaces;
using Apex.Services.Printing.Resilience;
using Apex.Services.Printing.VendorDetection;
using Microsoft.AspNetCore.Mvc;
using System.Drawing.Printing;

namespace Apex.API.Endpoints
{
    public static class PrinterEndpoints
    {
        public static IEndpointRouteBuilder MapPrinterEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/printers").WithTags("Printers");

            // ── List all installed printers ────────────────────────────────
            group.MapGet("/", async () =>
            {
                var printers = await Task.Run(() =>
                    PrinterSettings.InstalledPrinters
                        .Cast<string>()
                        .Select(name =>
                        {
                            var meta    = VendorDetectionEngine.Instance.GetPrinterMetadata(name);
                            int health  = CircuitBreakerManager.Instance.GetHealthScore(name);
                            var circuit = CircuitBreakerManager.Instance.GetState(name).ToString();

                            return new
                            {
                                name,
                                vendor       = meta?.Vendor.ToString() ?? "Unknown",
                                isOnline     = meta?.IsOnline ?? false,
                                supportsColor = meta?.SupportsColor ?? false,
                                supportsDuplex = meta?.SupportsDuplex ?? false,
                                connectionType = meta?.ConnectionType.ToString() ?? "Unknown",
                                healthScore  = health,
                                circuitState = circuit,
                                primaryLanguage = meta?.Capabilities?.SupportsPostScript == true ? "PostScript"
                                               : meta?.Capabilities?.SupportsPcl == true        ? "PCL"
                                               : meta?.Capabilities?.SupportsEscP == true       ? "ESC/P"
                                               : "GDI"
                            };
                        })
                        .ToList());

                return Results.Ok(printers);
            })
            .WithSummary("Get all installed printers with health status");

            // ── Get specific printer status ────────────────────────────────
            group.MapGet("/{printerName}", async (string printerName) =>
            {
                var meta = await Task.Run(() =>
                    VendorDetectionEngine.Instance.GetPrinterMetadata(printerName));

                if (meta == null)
                    return Results.NotFound(new { error = $"Printer '{printerName}' not found" });

                int    health  = CircuitBreakerManager.Instance.GetHealthScore(printerName);
                var    circuit = CircuitBreakerManager.Instance.GetState(printerName);
                var    cb      = CircuitBreakerManager.Instance.GetOrCreate(printerName);

                return Results.Ok(new
                {
                    name          = meta.Name,
                    vendor        = meta.Vendor.ToString(),
                    model         = meta.Model,
                    driverName    = meta.DriverName,
                    isOnline      = meta.IsOnline,
                    isNetworkPrinter = meta.IsNetworkPrinter,
                    portName      = meta.PortName,
                    connectionType = meta.ConnectionType.ToString(),
                    supportsColor  = meta.SupportsColor,
                    supportsDuplex = meta.SupportsDuplex,
                    capabilities  = new
                    {
                        postScript = meta.Capabilities?.SupportsPostScript,
                        pcl        = meta.Capabilities?.SupportsPcl,
                        escP       = meta.Capabilities?.SupportsEscP,
                        maxDpi     = meta.Capabilities?.MaxDpi
                    },
                    health = new
                    {
                        score        = health,
                        circuitState = circuit.ToString(),
                        failures     = cb.ConsecutiveFailures,
                        openedAt     = cb.OpenedAt
                    }
                });
            })
            .WithSummary("Get printer details and health");

            // ── Reset circuit breaker ──────────────────────────────────────
            group.MapPost("/{printerName}/reset", ([FromRoute] string printerName) =>
            {
                CircuitBreakerManager.Instance.Reset(printerName);
                return Results.Ok(new { message = $"تم إعادة تشغيل طابعة: {printerName}" });
            })
            .WithSummary("Reset printer circuit breaker");

            return app;
        }
    }
}
