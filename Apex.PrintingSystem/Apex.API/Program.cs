using Apex.Services.Printing.Queue;
using Apex.Services.Printing.Resilience;
using Apex.Services.Printing;
using Apex.API.Endpoints;
using Apex.API.Hubs;

var builder = WebApplication.CreateBuilder(args);

// ── Services ──────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Apex Print API", Version = "v1" });
});

builder.Services.AddCors(o =>
    o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

builder.Services.AddSignalR();
builder.Services.AddHostedService<PrintHubService>();

// ── App ───────────────────────────────────────────────────────────────────
var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Apex Print API v1"));
app.UseCors();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapHub<PrintHub>("/hubs/print");

// ── Start queue system ────────────────────────────────────────────────────
PrintJobQueueManager.Instance.Start();
PrintSpoolerWatcher.Instance.Start();

// ── Map Endpoints ─────────────────────────────────────────────────────────
app.MapPrintEndpoints();
app.MapPrinterEndpoints();
app.MapAnalyticsEndpoints();

app.Run("http://localhost:5175");
