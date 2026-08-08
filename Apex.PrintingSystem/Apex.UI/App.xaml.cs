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

        // Held for the app lifetime — releases automatically on process exit.
        private static System.Threading.Mutex? _singleInstanceMutex;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        /// <summary>
        /// Enforces one running copy. Two instances share the same SQLite database
        /// and state files, which risks corruption — so a second launch activates
        /// the existing window and exits.
        /// </summary>
        private bool EnsureSingleInstance()
        {
            _singleInstanceMutex = new System.Threading.Mutex(
                initiallyOwned: true, @"Local\ApexPrintOS_SingleInstance", out bool createdNew);
            if (createdNew) return true;

            // Bring the existing instance's window to the front, then bail out.
            try
            {
                var current = System.Diagnostics.Process.GetCurrentProcess();
                foreach (var p in System.Diagnostics.Process.GetProcessesByName(current.ProcessName))
                {
                    if (p.Id != current.Id && p.MainWindowHandle != IntPtr.Zero)
                    {
                        ShowWindow(p.MainWindowHandle, 9);   // SW_RESTORE
                        SetForegroundWindow(p.MainWindowHandle);
                        break;
                    }
                }
            }
            catch { /* best-effort activation */ }
            return false;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Route service-layer localization through the active language dictionary.
            // TryFindResource is UI-thread-affine, so the service layer (which may call
            // from background threads) is marshalled onto the Dispatcher via L().
            Apex.Core.Localization.AppLocalizer.Resolver = ViewModels.ViewModelBase.L;

            if (!EnsureSingleInstance())
            {
                Shutdown();
                return;
            }

            ShutdownMode = ShutdownMode.OnLastWindowClose;
            InstallGlobalExceptionHandlers();

            try
            {
                // ── License / Trial check ──
                var licenseResult = LicenseManager.Validate();
                if (!licenseResult.IsValid)
                {
                    var activationVm = new ActivationViewModel { ValidationResult = licenseResult };
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

                // Create and show main window
                var mainViewModel = _serviceProvider.GetRequiredService<MainViewModel>();
                var mainWindow = _serviceProvider.GetService<MainWindow>() ?? new MainWindow();
                mainWindow.DataContext = mainViewModel;
                mainWindow.Show();

                // Start print queue
                try
                {
                    Apex.Services.Printing.Queue.PrintJobQueueManager.Instance.Start();
                }
                catch { /* queue will start on demand if needed */ }

                // Start printer monitoring service
                try
                {
                    var monitoringService = _serviceProvider.GetRequiredService<Apex.Services.PrinterMonitoringService>();
                    monitoringService.StartAsync(System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                }
                catch { /* monitoring will degrade gracefully if WMI is unavailable */ }
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
            {
                var ex = e.ExceptionObject as Exception;
                WriteCrashLog("fatal_crash.log", "AppDomain.UnhandledException", ex);

                try
                {
                    var msg = ex is not null
                        ? $"البرنامج سيُغلق بسبب خطأ غير متوقع:\n\n{ex.GetType().Name}: {ex.Message}\n\nتم حفظ تقرير الخطأ."
                        : "البرنامج سيُغلق بسبب خطأ غير متوقع.\n\nتم حفظ تقرير الخطأ.";

                    var dispatcher = Current?.Dispatcher;
                    if (dispatcher is not null && !dispatcher.HasShutdownStarted)
                        dispatcher.Invoke(() =>
                            MessageBox.Show(msg, "خطأ فادح", MessageBoxButton.OK, MessageBoxImage.Error));
                    else
                        MessageBox.Show(msg, "خطأ فادح", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch { }
            };

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
                var dir = Path.Combine(
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
