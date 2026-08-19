using Apex.Core.Interfaces;
using Apex.Core.Models;
using Apex.Data;
using Apex.Data.Repositories;
using Apex.Services;
using Apex.UI.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;

namespace Apex.UI
{
    public static class ServiceExtensions
    {
        public static IServiceCollection AddApexServices(this IServiceCollection services)
        {
            // Database - use ProgramData for shared access or LocalApplicationData for per-user
            // TRANSIENT context lifetime: WPF has no per-request scope, so a shared
            // (scoped/root) DbContext gets hit concurrently by background timers,
            // queue processors and view-model initialisation — producing
            // "A second operation was started on this context instance".
            // A transient context gives every injected consumer its own instance;
            // SQLite handles the multiple short-lived connections fine.
            //
            // Lifetime note: every context consumer here is Scoped (repositories,
            // migration/init services, RoutingRulesEngine) — none is a Singleton or a
            // repeatedly-resolved Transient — so the number of live contexts is bounded
            // (one per consumer) and they are released with their owning provider/scope
            // (the startup init path resolves DbInitializer inside CreateScope()). If a
            // future per-operation, high-churn consumer is added, switch it to
            // IDbContextFactory<ApexDbContext> so it can create and dispose contexts on
            // demand rather than accumulating undisposed instances in the root provider.
            services.AddDbContext<ApexDbContext>(options =>
            {
                var dbFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "ApexPrintingSystem", "Database");

                // Ensure directory exists
                if (!Directory.Exists(dbFolder))
                {
                    Directory.CreateDirectory(dbFolder);
                }

                var dbPath = Path.Combine(dbFolder, "apex.db");
                options.UseSqlite($"Data Source={dbPath}");
            }, ServiceLifetime.Transient, ServiceLifetime.Singleton);

            // Repositories
            services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
            services.AddScoped<IRepository<Printer>, PrinterRepository>();
            services.AddScoped<IRepository<PrintJob>, PrintJobRepository>();
            services.AddScoped<IRepository<SystemSettings>, SettingsRepository>();
            services.AddScoped<Apex.Data.Migrations.AutoMigrationService>();
            services.AddScoped<DbInitializer>();

            // Services
            services.AddScoped<IPrinterService, PrinterService>();
            services.AddScoped<IPrintJobService, PrintJobService>();
            services.AddScoped<IJobDistributionService, JobDistributionService>();
            services.AddScoped<ISettingsService, SettingsService>();
            services.AddSingleton<ILoggerService, FileLoggerService>();
            services.AddSingleton<ILogReaderService, LogReaderService>();
            services.AddSingleton<IThemeService, ThemeService>();
            services.AddSingleton<ILocalizationService, LocalizationService>();
            services.AddSingleton<IPrinterDiscoveryService, PrinterDiscoveryService>();
            services.AddScoped<IDatabaseHealthService, DatabaseHealthService>();

            // Monitoring (started manually in App.xaml.cs — AddHostedService is a no-op on bare ServiceCollection)
            services.AddSingleton<PrinterMonitoringService>();

            // Same instance behind the probe: the print path asks it whether a station
            // can take work, and a second instance would answer from an empty cache.
            services.AddSingleton<IPrinterHealthProbe>(sp =>
                sp.GetRequiredService<PrinterMonitoringService>());

            // Print Manager Services
            services.AddSingleton<IFileIngestService, FileIngestService>();
            services.AddSingleton<ILoadBalancer, LoadBalancer>();
            services.AddScoped<IRoutingRulesEngine, RoutingRulesEngine>();
            services.AddScoped<IPrintJobManager, PrintJobManager>();
            services.AddScoped<Apex.Core.Interfaces.IDocumentConverter, Apex.Services.Conversion.DocumentConverter>();
            services.AddScoped<Apex.Core.Interfaces.IUniversalPrintPipeline, Apex.Services.Printing.UniversalPrintPipeline>();

            // Batch Print Services
            services.AddScoped<Apex.Core.Interfaces.IPrinterValidationService, Apex.Services.Printing.PrinterValidationService>();

            // 🔒 UNIFIED PRINT GATEWAY - Mandatory Entry Point for ALL printing
            services.AddSingleton<Apex.Core.Interfaces.IPageStreamEngine, Apex.Services.Printing.PdfPageStreamEngine>();
            services.AddSingleton<Apex.Core.Interfaces.IAdaptiveStreamDispatcher, Apex.Services.Printing.AdaptiveStreamDispatcher>();
            services.AddSingleton<Apex.Core.Interfaces.IFaultToleranceManager, Apex.Services.Printing.FaultToleranceManager>();
            services.AddSingleton<Apex.Core.Interfaces.IPrintGateway, Apex.Services.Printing.UnifiedPrintGateway>();
            services.AddScoped<Apex.Core.Interfaces.IPrintJobLogger, Apex.Services.Printing.PrintJobLogger>();
            services.AddScoped<Apex.Core.Interfaces.IPrintEngine, Apex.Services.Printing.PrintEngine>();
            // Singleton so the print queue and any running batch survive navigation.
            // A field operator peeked at Numbered Books mid-run and came back to an
            // empty queue with the job apparently cancelled — because navigation
            // disposes the scope (MainViewModel.NavigateTo) and this was scoped, so a
            // fresh empty instance was built each time. Safe as a singleton: printing
            // goes through the SmartPrintManager singleton, and none of this graph's
            // dependencies hold a DbContext (PrintJobLogger is Serilog, the converter
            // and validator are stateless), so #15's captive-context risk does not apply.
            services.AddSingleton<Apex.Services.Printing.BatchPrintJobManager>();

            // Distribution Services
            services.AddScoped<Apex.Services.Printing.PrintDispatcher>();
            services.AddScoped<Apex.Services.Printing.PrinterStatusService>();

            // Numbering Services
            // Transient (was Scoped): NumberingWizardViewModel is now a Singleton so its
            // state survives navigation, and a singleton must not depend on a scoped
            // service. NumberingService is safe either way — it holds no DbContext (it
            // news up its own orchestrator/composer/loader) — Transient just keeps the
            // lifetimes honest so enabling scope validation later won't trip on it.
            services.AddTransient<Apex.Services.Numbering.NumberingService>();
            services.AddScoped<Apex.NumberedBooksEngine.Core.INumberSequencer, Apex.NumberedBooksEngine.Core.NumberSequencer>();

            // Sheet-fill optimizer — no longer a user-facing module; kept as the
            // internal engine ImpositionService uses to fill a sheet with repeats.
            services.AddTransient<Apex.Services.PaperCutting.PaperCuttingOptimizerService>();

            // Imposition & Finishing
            services.AddTransient<Apex.Services.Imposition.ImpositionService>();
            services.AddTransient<Apex.Services.Imposition.PdfOperationsService>();
            services.AddTransient<Apex.Services.Imposition.PdfPageToolsService>();
            services.AddTransient<Apex.Services.Imposition.PdfImpositionEngine>();
            services.AddSingleton<Apex.Services.Imposition.ImpositionTemplateStore>();

            // Dialog Service
            services.AddSingleton<Apex.Core.Interfaces.IDialogService, Apex.UI.Services.DialogService>();
            services.AddSingleton<Apex.Core.Interfaces.IFileDialogService, Apex.UI.Services.FileDialogService>();

            // Cross-section workflow bridge ("send to printing" hand-off).
            services.AddSingleton<Apex.UI.Services.WorkflowHandoff>();

            // ViewModels
            services.AddSingleton<MainViewModel>();
            services.AddTransient<DashboardViewModel>();
            services.AddTransient<SettingsViewModel>();
            // Singleton: the print workspace (its file queue and active run) is a
            // single app-wide thing, not rebuilt on every visit — see the
            // BatchPrintJobManager note above. It holds only singletons now.
            services.AddSingleton<PrintManagerViewModel>();
            // Singletons: the "document" workspaces. Navigation (MainViewModel.NavigateTo)
            // disposes the DI scope and re-resolves the screen, so a Transient view-model
            // was rebuilt empty on every return — the operator lost the loaded file, the
            // number slots, the imposition result, or an unsaved design just by glancing
            // at another section. Each of these graphs was verified DbContext-free
            // (NumberingService/PdfPageToolsService/ImpositionService & co. new up their
            // own helpers), so they carry none of #15's captive-context risk, and none
            // clears its working state in InitializeAsync. So they can safely live for
            // the app's lifetime and keep their state across navigation.
            services.AddSingleton<NumberingWizardViewModel>();
            services.AddSingleton<TemplateDesignerViewModel>();
            services.AddSingleton<ImpositionViewModel>();
            services.AddSingleton<PageToolsViewModel>();
            // Transient (rebuilt each visit): these show live/system data and SHOULD
            // refresh on return, and hold no user-entered work worth preserving.
            services.AddTransient<DashboardViewModel>();
            services.AddTransient<SystemPerformanceViewModel>();
            services.AddTransient<LogViewerViewModel>();
            services.AddTransient<LicensingViewModel>();
            services.AddTransient<ColorCalibrationViewModel>();

            // Views
            services.AddSingleton<MainWindow>();

            return services;
        }
    }
}
