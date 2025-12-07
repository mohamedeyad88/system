using Apex.Core.Interfaces;
using Apex.Data;
using Apex.Data.Repositories;
using Apex.Services;
using Apex.UI.Modules;
using Apex.UI.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Apex.Core.Models;
using QuestPDF.Infrastructure;
using System.Windows;

namespace Apex.UI
{
    public partial class App : Application
    {
        public static IHost? AppHost { get; private set; }

        public App()
        {
            QuestPDF.Settings.License = LicenseType.Community;

            AppHost = Host.CreateDefaultBuilder()
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddApexServices();
                    services.AddSingleton<ICacheService, CacheService>();
                    
                    // Printing Subsystem
                    services.AddSingleton<Apex.Core.Interfaces.IPrinterValidationService, Apex.Services.Printing.PrinterValidationService>();
                    services.AddSingleton<Apex.Core.Interfaces.IPrintJobLogger, Apex.Services.Printing.PrintJobLogger>();
                    services.AddSingleton<Apex.Core.Interfaces.IPrintEngine, Apex.Services.Printing.PrintEngine>();
                    
                    // Advanced Distribution Module
                    services.AddSingleton<Apex.Services.Printing.PrintDispatcher>();
                    services.AddSingleton<Apex.Services.Printing.PrinterStatusService>();

                    // Batch Printing Module
                    services.AddSingleton<Apex.Services.Printing.FilePreparationService>();
                    services.AddSingleton<Apex.Services.Printing.BatchPrintJobManager>();
                    services.AddTransient<BatchPrintViewModel>();

                    // Free-Form Numbering Module
                    services.AddSingleton<Apex.Services.Numbering.NumberingService>();
                    services.AddTransient<Apex.UI.ViewModels.FreeFormEditorViewModel>();
                    services.AddTransient<Apex.UI.ViewModels.NumberingWizardViewModel>();

                    // Legacy (to be removed later)
                    services.AddSingleton<Apex.UI.Services.Printing.BatchPrintService>();
                    services.AddSingleton<Apex.UI.Services.Printing.DistributionPrintService>();
                    
                    // Distribution Printing (New Advanced Module)
                    services.AddSingleton<Apex.Services.Printing.ParallelPrintEngine>();
                    services.AddTransient<DistributionViewModel>();
                    
                    // Architecture Enhancements
                    services.AddSingleton<Apex.Services.Printing.PrintWorkerPool>();
                    services.AddSingleton<Apex.Services.Printing.FilePreparationWorkerPool>();
                    services.AddSingleton<Apex.Services.Printing.PrinterDispatchWorkerPool>();
                    services.AddSingleton<Apex.Services.Printing.PrePressValidator>();
                    services.AddSingleton<Apex.Services.Monitoring.SnmpPrinterMonitor>();
                    
                    // Quotation System
                    services.AddSingleton<Apex.Services.QuotationService>();
                    services.AddTransient<QuotationViewModel>();
                    
                    // Unified Operations Center
                    services.AddTransient<PrintOperationsViewModel>();
                    
                    // UI Services
                    services.AddSingleton<IDialogService, Apex.UI.Services.DialogService>();

                    // Background Services (Efficiency & Stability)
                    services.AddHostedService<Apex.Services.Maintenance.TempFileCleanupService>();
                    services.AddHostedService<Apex.Services.PrinterMonitoringService>();
                })
                .Build();
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                await AppHost!.StartAsync();

                // Global Exception Handling
                DispatcherUnhandledException += App_DispatcherUnhandledException;
                AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
                TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

                // Initialize Database
                using (var scope = AppHost.Services.CreateScope())
                {
                    var logger = scope.ServiceProvider.GetRequiredService<ILoggerService>();
                    logger.Log(LogLevel.Info, "Application Starting...", "App", "OnStartup");

                    var dbInitializer = scope.ServiceProvider.GetRequiredService<DbInitializer>();
                    await dbInitializer.InitializeAsync();
                    
                    // Load Language
                    var settingsService = scope.ServiceProvider.GetRequiredService<ISettingsService>();
                    var lang = await settingsService.GetValueAsync("Language", "en");
                    Apex.UI.Services.LocalizationService.Instance.SwitchLanguage(lang);
                    
                    logger.Log(LogLevel.Info, "Startup Complete", "App", "OnStartup");
                }

                var startupForm = AppHost.Services.GetRequiredService<MainWindow>();
                startupForm.DataContext = AppHost.Services.GetRequiredService<MainViewModel>();
                startupForm.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Startup Error: {ex.Message}\n\n{ex.StackTrace}", "Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            using (var scope = AppHost!.Services.CreateScope())
            {
                var logger = scope.ServiceProvider.GetRequiredService<ILoggerService>();
                logger.Log(LogLevel.Info, "Application Exiting", "App", "OnExit");
            }
            await AppHost!.StopAsync();
            base.OnExit(e);
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            LogGlobalError(e.Exception, "UI Thread Error");
            e.Handled = true; 
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            LogGlobalError(e.ExceptionObject as Exception, "Non-UI Thread Error");
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            LogGlobalError(e.Exception, "Unobserved Task Error");
            e.SetObserved();
        }

        private void LogGlobalError(Exception? ex, string source)
        {
            try
            {
                using (var scope = AppHost!.Services.CreateScope())
                {
                    var logger = scope.ServiceProvider.GetRequiredService<ILoggerService>();
                    logger.Log(LogLevel.Critical, $"Global Exception ({source})", "App", "GlobalHandler", ex);
                }
                MessageBox.Show($"An unexpected error occurred: {ex?.Message}\n\nCheck logs for details.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch
            {
                MessageBox.Show("Fatal Error: Logging failed.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

