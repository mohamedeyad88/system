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

            // Open the print log now rather than on the first print.
            //
            // PrintLogger is static, so its file was only created once something
            // printed. Anything that went wrong before that — a licence problem, a
            // printer that would not enumerate — left no log at all, which is exactly
            // when a field report needs evidence.
            try
            {
                Apex.Services.Logging.PrintLogger.Info(
                    "=== Apex Print OS {Version} started ===",
                    System.Reflection.Assembly.GetExecutingAssembly()
                        .GetName().Version?.ToString() ?? "?");
            }
            catch { /* logging must never block startup */ }

            try
            {
                // ── Soft revocation (best-effort, fail-open) ──
                // If this device is online and its Apex license was revoked on the
                // server (e.g. after a refund), drop the local license so the app
                // returns to activation-required. Offline machines are never locked
                // out — the "works without internet" promise is preserved. Bounded so
                // a slow/absent server never delays startup by more than a few seconds.
                try
                {
                    if (LicenseManager.HasFullLicense())
                    {
                        var deviceId = LicenseManager.GetDeviceInfo().DeviceId;
                        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                        // Run off the UI thread: sync-over-async on the dispatcher would
                        // deadlock if the awaited call captured the UI context.
                        bool revoked = System.Threading.Tasks.Task.Run(
                            () => new Apex.UI.Services.LicenseRevocationService()
                                .IsRevokedAsync(deviceId, cts.Token)).GetAwaiter().GetResult();
                        if (revoked) LicenseManager.DeleteLicense();
                    }
                }
                catch { /* revocation must never block or crash startup */ }
                Apex.Services.Logging.PrintLogger.Info("startup: 1 revocation checked");

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

                // Initialize default language — Arabic first (the audience is Arabic
                // print shops; the whole UI, including Template Designer, is localised).
                try
                {
                    Services.LocalizationService.Instance.SwitchLanguage("ar");
                }
                catch { /* continue with default */ }

                // Configure services
                var services = new ServiceCollection();
                services.AddApexServices();
                _serviceProvider = services.BuildServiceProvider();
                Apex.Services.Logging.PrintLogger.Info("startup: 2 DI built");

                // Verify critical services
                var scopeFactory = _serviceProvider.GetService<IServiceScopeFactory>()
                    ?? throw new InvalidOperationException("IServiceScopeFactory is not available.");

                // Initialize database
                using (var scope = scopeFactory.CreateScope())
                {
                    var dbInitializer = scope.ServiceProvider.GetRequiredService<Apex.Data.DbInitializer>();
                    System.Threading.Tasks.Task.Run(() => dbInitializer.InitializeAsync()).GetAwaiter().GetResult();
                }
                Apex.Services.Logging.PrintLogger.Info("startup: 3 db initialized");

                // Create and show main window
                var mainViewModel = _serviceProvider.GetRequiredService<MainViewModel>();
                Apex.Services.Logging.PrintLogger.Info("startup: 4 main VM created");
                var mainWindow = _serviceProvider.GetService<MainWindow>() ?? new MainWindow();
                Apex.Services.Logging.PrintLogger.Info("startup: 5 main window constructed");
                mainWindow.DataContext = mainViewModel;
                mainWindow.Show();
                Apex.Services.Logging.PrintLogger.Info("startup: 6 main window shown");

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
                    System.Threading.Tasks.Task.Run(() => monitoringService.StartAsync(System.Threading.CancellationToken.None)).GetAwaiter().GetResult();
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
                ReportToUser(e.Exception);
            };

            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                WriteCrashLog("crash_log.txt", "UnobservedTaskException", e.Exception);
                e.SetObserved();
            };
        }

        private bool _errorDialogOpen;
        private string? _lastReportedError;
        private DateTime _lastReportUtc = DateTime.MinValue;

        /// <summary>
        /// Shows an unexpected error without turning it into a crash.
        ///
        /// This used to call MessageBox.Show straight from the handler. When the
        /// exception came from inside a layout pass — a template referencing a
        /// resource that is not there, say — the modal dialog pumped messages, which
        /// ran layout again, which threw again, which opened another dialog inside
        /// the first. On 2026-08-05 that nested twelve times in one second and the
        /// process died of a stack overflow (0xc00000fd, faulting in dwrite.dll —
        /// the deepest frame of the nested layout, not the culprit).
        ///
        /// Two things prevent that recurring: the dialog is posted back to the queue
        /// so it opens after the current pass has unwound, and a repeat of the same
        /// error is logged but not shown again.
        /// </summary>
        private void ReportToUser(Exception? ex)
        {
            if (ex is null) return;

            var message = ex.Message;
            var now = DateTime.UtcNow;

            if (_errorDialogOpen) return;

            // A fault during layout repeats every frame. Say it once.
            if (message == _lastReportedError && (now - _lastReportUtc) < TimeSpan.FromSeconds(30))
                return;

            _lastReportedError = message;
            _lastReportUtc = now;

            var dispatcher = Current?.Dispatcher;
            if (dispatcher is null || dispatcher.HasShutdownStarted) return;

            dispatcher.BeginInvoke(new Action(() =>
            {
                _errorDialogOpen = true;
                try
                {
                    MessageBox.Show(
                        $"خطأ غير متوقع:\n{message}",
                        "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    _errorDialogOpen = false;
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
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
