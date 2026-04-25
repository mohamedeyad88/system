using System.Windows;
using System;
using Microsoft.Extensions.DependencyInjection;
using Apex.UI.ViewModels;
using Apex.UI.Views;
using System.IO;
using System.Threading.Tasks;
using Apex.Licensing;

namespace Apex.UI
{
    public partial class App : Application
    {
        private IServiceProvider? _serviceProvider;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // ── Prevent WPF from shutting down when LoginWindow closes ────────
            // Default OnLastWindowClose would kill the app between ShowDialog()
            // returning and mainWindow.Show() being called.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            InstallGlobalExceptionHandlers();

            try
            {
                // ── License / Trial check ──
                var licenseResult = LicenseManager.Validate();
                if (!licenseResult.IsValid)
                {
                    var activationVm  = new ActivationViewModel { ValidationResult = licenseResult };
                    var activationWin = new ActivationView(activationVm);
                    activationWin.ShowDialog();
                    Shutdown();
                    return;
                }

                // Initialize default language
                try
                {
                    Services.LocalizationService.Instance.SwitchLanguage("en");
                }
                catch { /* continue with default */ }

                // Configure services
                var services = new ServiceCollection();
                services.AddApexServices();
                _serviceProvider = services.BuildServiceProvider();

                // Verify critical services
                var scopeFactory = _serviceProvider.GetService<IServiceScopeFactory>()
                    ?? throw new InvalidOperationException("IServiceScopeFactory is not available.");

                // Initialize database
                using (var scope = scopeFactory.CreateScope())
                {
                    var dbInitializer = scope.ServiceProvider.GetRequiredService<Apex.Data.DbInitializer>();
                    dbInitializer.InitializeAsync().GetAwaiter().GetResult();
                }

                // ── Login Gate ────────────────────────────────────────────────
                var loginWindow = new Views.LoginWindow();
                bool? loginResult = loginWindow.ShowDialog();
                if (loginResult != true)
                {
                    Shutdown();
                    return;
                }

                // Create and show main window
                var mainViewModel = _serviceProvider.GetRequiredService<MainViewModel>();
                var mainWindow    = _serviceProvider.GetService<MainWindow>() ?? new MainWindow();
                mainWindow.DataContext = mainViewModel;
                // Shut down when the main window closes
                mainWindow.Closed += (_, __) => Shutdown();
                mainWindow.Show();

                // Start print queue
                try
                {
                    Apex.Services.Printing.Queue.PrintJobQueueManager.Instance.Start();
                }
                catch { /* queue will start on demand if needed */ }

                // Start scheduled report manager
                try
                {
                    Apex.Services.Analytics.ScheduledReportManager.Instance.EnsureDefaultSchedules();
                    Apex.Services.Analytics.ScheduledReportManager.Instance.Start();
                }
                catch { /* scheduled reports will be initialized on demand if needed */ }
            }
            catch (Exception ex)
            {
                WriteCrashLog("crash_log.txt", "OnStartup", ex);
                MessageBox.Show(
                    $"خطأ فادح عند بدء التشغيل:\n\n{ex.Message}\n\nتم حفظ تقرير الخطأ.",
                    "خطأ فادح", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        private void InstallGlobalExceptionHandlers()
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                WriteCrashLog("fatal_crash.log", "AppDomain.UnhandledException", e.ExceptionObject as Exception);

            Current.DispatcherUnhandledException += (_, e) =>
            {
                WriteCrashLog("crash_log.txt", "DispatcherUnhandledException", e.Exception);
                e.Handled = true;
                MessageBox.Show(
                    $"خطأ غير متوقع:\n{e.Exception.Message}",
                    "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            };

            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                WriteCrashLog("crash_log.txt", "UnobservedTaskException", e.Exception);
                e.SetObserved();
            };
        }

        private static void WriteCrashLog(string fileName, string source, Exception? ex)
        {
            try
            {
                var dir  = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ApexPrintingSystem");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, fileName);
                File.AppendAllText(path,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\n{ex}\n\n");
            }
            catch { }
        }
    }
}
