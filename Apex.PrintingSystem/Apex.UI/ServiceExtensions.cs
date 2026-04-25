using Apex.Core.Interfaces;
using Apex.Core.Models;
using Apex.Data;
using Apex.Data.Repositories;
using Apex.Services;
using Apex.UI.Modules;
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
            });

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
            
            // Monitoring
            services.AddSingleton<PrinterMonitoringService>();
            services.AddHostedService<PrinterMonitoringService>(provider => provider.GetRequiredService<PrinterMonitoringService>());

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
            
            // Dialog Service
            services.AddSingleton<Apex.Core.Interfaces.IDialogService, Apex.UI.Services.DialogService>();
            
            // Business Services
            services.AddScoped<QuotationService>();
            services.AddScoped<IInvoiceService, InvoiceService>();

            // ViewModels
            services.AddSingleton<MainViewModel>();
            services.AddTransient<DashboardViewModel>();
            services.AddTransient<PrintersViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<PrintManagerViewModel>();
            services.AddTransient<NumberedBooksViewModel>();
            services.AddTransient<NumberingWizardViewModel>();
            services.AddTransient<SystemPerformanceViewModel>();
            services.AddTransient<LogViewerViewModel>();
            services.AddTransient<DistributionViewModel>();
            services.AddTransient<QuotationViewModel>();
            services.AddTransient<BatchPrintViewModel>();
            services.AddTransient<PrintOperationsViewModel>();
            services.AddTransient<LicensingViewModel>();
            services.AddTransient<AnalyticsDashboardViewModel>();
            services.AddTransient<UserManagementViewModel>();
            services.AddTransient<ReportsViewModel>();
            services.AddTransient<LoadBalancerViewModel>();
            services.AddTransient<ColorCalibrationViewModel>();
            services.AddTransient<TemplateDesignerViewModel>();

            // Views
            services.AddSingleton<MainWindow>();

            return services;
        }
    }
}
