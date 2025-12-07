using Apex.Core.Interfaces;
using Apex.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Apex.UI.Modules
{
    public static class ServicesModule
    {
        public static IServiceCollection AddApexServices(this IServiceCollection services)
        {
            // Core Services
            services.AddSingleton<ICacheService, CacheService>();
            services.AddSingleton<ILoggerService, FileLoggerService>();
            services.AddSingleton<ILogReaderService, LogReaderService>();
            services.AddSingleton<LogMaintenanceService>();
            services.AddSingleton<ExceptionRecoveryService>();
            
            // Domain Services
            services.AddScoped<IPrinterService, PrinterService>();
            services.AddScoped<IPrintJobService, PrintJobService>();
            services.AddScoped<IJobDistributionService, JobDistributionService>();
            services.AddScoped<ISettingsService, SettingsService>();
            services.AddSingleton<IThemeService, ThemeService>();
            services.AddSingleton<ILocalizationService, LocalizationService>();
            services.AddSingleton<IPrinterDiscoveryService, PrinterDiscoveryService>();
            services.AddScoped<IDatabaseHealthService, DatabaseHealthService>();

            // Print Manager Services
            services.AddSingleton<IFileIngestService, FileIngestService>();
            services.AddSingleton<ILoadBalancer, LoadBalancer>();
            services.AddScoped<IRoutingRulesEngine, RoutingRulesEngine>();
            services.AddScoped<IPrintJobManager, PrintJobManager>();
            services.AddSingleton<PrintJobProcessor>();
            services.AddSingleton<PrinterMonitoringService>();
            
            services.AddSingleton<INavigationService, NavigationService>();

            return services;
        }
    }
}
