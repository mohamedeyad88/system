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
            services.AddScoped<Apex.Services.Printing.BatchPrintJobManager>();

            // Distribution Services
            services.AddScoped<Apex.Services.Printing.PrintDispatcher>();
            services.AddScoped<Apex.Services.Printing.PrinterStatusService>();

            // Numbering Services
            services.AddScoped<Apex.Services.Numbering.NumberingService>();
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

            // ViewModels
            services.AddSingleton<MainViewModel>();
            services.AddTransient<DashboardViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<PrintManagerViewModel>();
            services.AddTransient<NumberingWizardViewModel>();
            services.AddTransient<SystemPerformanceViewModel>();
            services.AddTransient<LogViewerViewModel>();
            services.AddTransient<LicensingViewModel>();
            services.AddTransient<ColorCalibrationViewModel>();
            services.AddTransient<TemplateDesignerViewModel>();
            services.AddTransient<ImpositionViewModel>();
            services.AddTransient<PageToolsViewModel>();

            // Views
            services.AddSingleton<MainWindow>();

            return services;
        }
    }
}
