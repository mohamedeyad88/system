using Apex.Core.Interfaces;
using Apex.Core.Models;
using Apex.Data;
using Apex.Data.Repositories;
using Apex.Services;
using Apex.UI.Modules;
using Apex.UI.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Apex.UI
{
    public static class ServiceExtensions
    {
        public static IServiceCollection AddApexServices(this IServiceCollection services)
        {
            // Database
            services.AddDbContext<ApexDbContext>(options =>
            {
                var dbPath = @"C:\ProgramData\ApexPrintingSystem\Database\apex.db";
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

            // ViewModels
            services.AddSingleton<MainViewModel>();
            services.AddTransient<PrintersViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<PrintManagerViewModel>();
            services.AddTransient<NumberedBooksViewModel>();
            services.AddTransient<SystemPerformanceViewModel>();
            services.AddTransient<PrinterDiagnosticsViewModel>();

            // Views
            services.AddSingleton<MainWindow>();

            return services;
        }
    }
}
