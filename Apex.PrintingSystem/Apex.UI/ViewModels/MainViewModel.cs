using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace Apex.UI.ViewModels
{
    public partial class MainViewModel : ViewModelBase
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private IServiceScope? _currentScope;

        [ObservableProperty]
        private ViewModelBase? _currentViewModel;

        // ── Active-state properties for sidebar navigation highlight ──
        public bool IsDashboardActive => CurrentViewModel is DashboardViewModel;
        public bool IsPrintManagerActive => CurrentViewModel is PrintManagerViewModel;
        public bool IsNumberedBooksActive => CurrentViewModel is NumberingWizardViewModel;
        public bool IsPerformanceActive => CurrentViewModel is SystemPerformanceViewModel;
        public bool IsSettingsActive => CurrentViewModel is SettingsViewModel;
        public bool IsLicensingActive => CurrentViewModel is LicensingViewModel;
        public bool IsColorCalibrationActive => CurrentViewModel is ColorCalibrationViewModel;
        public bool IsTemplateDesignerActive => CurrentViewModel is TemplateDesignerViewModel;
        public bool IsImpositionActive => CurrentViewModel is ImpositionViewModel;
        public bool IsPageToolsActive => CurrentViewModel is PageToolsViewModel;

        // ── Shell-wide live print status ─────────────────────────────────────
        // The batch runs in a singleton, so its progress can be shown from ANY
        // section: the operator sets up the next job while a run prints, and still
        // sees how it is going without navigating back to Printing.
        private readonly Apex.Services.Printing.BatchPrintJobManager _batch;
        [ObservableProperty] private bool _isPrintingGlobally;
        [ObservableProperty] private double _printProgress;
        [ObservableProperty] private string _printStatusText = "";

        private readonly Apex.UI.Services.WorkflowHandoff _handoff;

        public MainViewModel(IServiceScopeFactory scopeFactory, Apex.Services.Printing.BatchPrintJobManager batch,
            Apex.UI.Services.WorkflowHandoff handoff)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _batch = batch ?? throw new ArgumentNullException(nameof(batch));
            _handoff = handoff ?? throw new ArgumentNullException(nameof(handoff));
            HookPrintStatus();
            // Workflow chain: a producer section asks to send its output to Printing;
            // the shell navigates there and drops the file into the queue.
            _handoff.SendToPrintingRequested += OnSendToPrinting;
            _ = CheckForUpdatesAsync();   // fire-and-forget; never blocks startup
            CheckLicenseNotice();
        }

        // ── Closing the window in the middle of a run ────────────────────────
        // Reported from the floor: "after the printing finished and the program was
        // closed, the commands stopped and never arrived." They had not all arrived.
        // The batch is executed inside this process — every page is rendered here and
        // handed to the spooler one copy at a time, and a station that runs dry holds
        // its copy until someone attends to it — so whatever the run still owes is
        // lost the moment the process exits. Nothing warned about that, and nothing
        // recorded it either: the operator saw the printer stop and had no way to know
        // work had been thrown away.

        /// <summary>What closing the window right now would cost.</summary>
        public readonly record struct ShutdownPrompt(bool Ask, int OutstandingCopies);

        /// <summary>
        /// Ask before closing only while a run is actually in flight. A finished run
        /// that still has sheets coming out of the printer is NOT a reason to ask:
        /// those pages are the spooler's now and print whether Apex is open or not.
        /// </summary>
        public static ShutdownPrompt ShutdownPromptFor(bool runInFlight, int outstandingCopies) =>
            runInFlight && outstandingCopies > 0
                ? new ShutdownPrompt(true, outstandingCopies)
                : new ShutdownPrompt(false, 0);

        /// <summary>The live version of <see cref="ShutdownPromptFor"/>.</summary>
        public ShutdownPrompt CurrentShutdownPrompt() =>
            ShutdownPromptFor(_batch.IsRunning, _batch.OutstandingCopies);

        /// <summary>
        /// The operator chose to close anyway: stop the run and leave a record of what
        /// it still owed, so a shop asking "why did half the order not print?" has an
        /// answer in the print log.
        /// </summary>
        public void AbandonPrintRunForShutdown()
        {
            int lost = _batch.OutstandingCopies;
            Apex.Services.Logging.PrintLogger.Warning(
                "[Shell] The operator closed Apex during a print run. {Copies} copies were never sent.",
                lost);
            _batch.CancelBatch();
        }

        private void OnSendToPrinting(string filePath) => OnUi(() =>
        {
            NavigateToPrintManager();
            if (CurrentViewModel is PrintManagerViewModel pm)
                pm.IngestExternalFile(filePath);
        });

        private void HookPrintStatus()
        {
            _batch.OnBatchStatusChanged += (_, s) => OnUi(() =>
            {
                if (s == "Starting batch...")
                { IsPrintingGlobally = true; PrintProgress = 0; PrintStatusText = L("Shell_PrintStarting"); }
            });
            _batch.OnBatchProgressChanged += (_, p) => OnUi(() =>
            {
                PrintProgress = p.PercentComplete;
                // Still running while any job is unattempted (a held printer keeps the
                // bar up); it clears itself once every job has been attempted.
                IsPrintingGlobally = p.TotalJobs > 0 && p.AttemptedJobs < p.TotalJobs;
                PrintStatusText = Lf("Shell_Printing", p.CompletedJobs, p.TotalJobs);
            });
            _batch.OnPrinterHeld += (_, e) => OnUi(() =>
            {
                IsPrintingGlobally = true;
                PrintStatusText = Lf("Shell_PrintHeld", e.PrinterName);
            });
        }

        private static void OnUi(Action action)
            => System.Windows.Application.Current?.Dispatcher.InvokeAsync(action);

        // ── App update notice (server-driven) ─────────────────────────────────
        [ObservableProperty] private bool _updateAvailable;
        [ObservableProperty] private string _updateVersion = "";
        [ObservableProperty] private string _updateNotes = "";
        private string _updateUrl = "";

        private async Task CheckForUpdatesAsync()
        {
            try
            {
                string current =
                    System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
                var info = await new Services.UpdateCheckService().CheckAsync(current);
                if (info is { Available: true } && !string.IsNullOrWhiteSpace(info.DownloadUrl))
                {
                    _updateUrl = info.DownloadUrl;
                    UpdateVersion = info.LatestVersion;
                    UpdateNotes = info.Notes;
                    UpdateAvailable = true;
                }
            }
            catch
            {
                // Best-effort: offline or server unreachable — stay silent.
            }
        }

        [RelayCommand]
        private void OpenUpdateLink()
        {
            if (string.IsNullOrWhiteSpace(_updateUrl)) return;
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(_updateUrl) { UseShellExecute = true });
            }
            catch { /* browser launch failed — ignore */ }
        }

        [RelayCommand]
        private void DismissUpdate() => UpdateAvailable = false;

        // ── Licence ending soon ──────────────────────────────────────────────
        // The site sells one- and two-year subscriptions, and an expired one used to
        // surface only as the activation window on the morning it ran out — mid-week,
        // with a job waiting. The shop now hears about it while there is time to renew.
        [ObservableProperty] private bool _licenseNoticeVisible;
        [ObservableProperty] private string _licenseNoticeText = "";
        [ObservableProperty] private string _licenseNoticeAction = "";
        private string _licenseNoticeReason = "";

        /// <summary>Trial: last 2 days. Subscription: last 30 days. Perpetual: never.</summary>
        public static (bool Show, bool IsTrial, int Days) LicenseNoticeFor(Apex.Licensing.ValidationResult r)
        {
            if (!r.IsValid || r.ExpiresUtc is null) return (false, false, 0);
            int days = Math.Max(1, r.DaysRemaining);
            if (r.Type == Apex.Licensing.LicenseType.Trial) return (r.DaysRemaining <= 2, true, days);
            if (r.ExpiresUtc.Value.Year >= 9999) return (false, false, 0);
            return (r.DaysRemaining <= 30, false, days);
        }

        private void CheckLicenseNotice()
        {
            try
            {
                var r = Apex.Licensing.LicenseManager.Validate();
                var (show, trial, days) = LicenseNoticeFor(r);
                if (!show) return;

                LicenseNoticeText = trial
                    ? Lf("Notice_TrialEnding", days)
                    : Lf("Notice_LicenseEnding", days, r.ExpiresUtc!.Value.ToLocalTime().ToString("yyyy-MM-dd"));
                LicenseNoticeAction = L(trial ? "Notice_Buy" : "Notice_Renew");
                _licenseNoticeReason = trial ? "trial-ending" : "renewal";
                LicenseNoticeVisible = true;
            }
            catch { /* a notice is never worth blocking the shell */ }
        }

        [RelayCommand]
        private void OpenLicenseNoticeLink() => Services.WebLinks.OpenPricing(_licenseNoticeReason);

        [RelayCommand]
        private void DismissLicenseNotice() => LicenseNoticeVisible = false;

        partial void OnCurrentViewModelChanged(ViewModelBase? value)
        {
            if (value != null)
                _ = InitializeCurrentViewModelAsync(value);

            OnPropertyChanged(nameof(IsDashboardActive));
            OnPropertyChanged(nameof(IsPrintManagerActive));
            OnPropertyChanged(nameof(IsNumberedBooksActive));
            OnPropertyChanged(nameof(IsPerformanceActive));
            OnPropertyChanged(nameof(IsSettingsActive));
            OnPropertyChanged(nameof(IsLicensingActive));
            OnPropertyChanged(nameof(IsColorCalibrationActive));
            OnPropertyChanged(nameof(IsTemplateDesignerActive));
            OnPropertyChanged(nameof(IsImpositionActive));
        }

        private async Task InitializeCurrentViewModelAsync(ViewModelBase viewModel)
        {
            try
            {
                await Task.Run(async () =>
                {
                    await viewModel.InitializeAsync().ConfigureAwait(false);
                }).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _ = Task.Run(() =>
                {
                    try
                    {
                        var logPath = System.IO.Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "ApexPrintingSystem", "Logs", "viewmodel_error.log");
                        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath)!);
                        System.IO.File.AppendAllText(logPath,
                            $"[{DateTime.Now}] Error initializing {viewModel.GetType().Name}:\n{ex}\n\n");
                    }
                    catch { }
                });
            }
        }

        private void NavigateTo<T>() where T : ViewModelBase
        {
            try
            {
                _currentScope?.Dispose();
                _currentScope = _scopeFactory.CreateScope();
                var viewModel = _currentScope.ServiceProvider.GetRequiredService<T>();
                CurrentViewModel = viewModel;
            }
            catch (Exception ex)
            {
                _ = Task.Run(() =>
                {
                    try
                    {
                        var logPath = System.IO.Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "ApexPrintingSystem", "Logs", "navigation_error.log");
                        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath)!);
                        System.IO.File.AppendAllText(logPath,
                            $"[{DateTime.Now}] Error navigating to {typeof(T).Name}:\n{ex.Message}\n{ex.StackTrace}\n\n");
                    }
                    catch { }
                });

                System.Windows.MessageBox.Show(
                    $"Error navigating to {typeof(T).Name}:\n{ex.Message}",
                    "Navigation Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void NavigateToDashboard() => NavigateTo<DashboardViewModel>();

        [RelayCommand]
        public void NavigateToPrintManager() => NavigateTo<PrintManagerViewModel>();

        [RelayCommand]
        public void NavigateToNumberedBooks() => NavigateTo<NumberingWizardViewModel>();

        [RelayCommand]
        public void NavigateToPerformance() => NavigateTo<SystemPerformanceViewModel>();

        [RelayCommand]
        public void NavigateToSettings() => NavigateTo<SettingsViewModel>();

        [RelayCommand]
        public void NavigateToLicensing() => NavigateTo<LicensingViewModel>();

        [RelayCommand]
        public void SwitchLanguage(string cultureCode)
        {
            Services.LocalizationService.Instance.SwitchLanguage(cultureCode);
            _ = PersistLanguageAsync(cultureCode);
        }

        /// <summary>The sidebar toggle is the switch operators use; remember it for next start.</summary>
        private async System.Threading.Tasks.Task PersistLanguageAsync(string cultureCode)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var settings = scope.ServiceProvider.GetRequiredService<Apex.Core.Interfaces.ISettingsService>();
                await settings.SetValueAsync(Services.LocalizationService.LanguageSettingKey, cultureCode);
            }
            catch { /* a language that is not remembered is not worth an error dialog */ }
        }

        [RelayCommand]
        public void NavigateToColorCalibration() => NavigateTo<ColorCalibrationViewModel>();

        [RelayCommand]
        public void NavigateToTemplateDesigner() => NavigateTo<TemplateDesignerViewModel>();

        [RelayCommand]
        public void NavigateToImposition() => NavigateTo<ImpositionViewModel>();

        [RelayCommand]
        public void NavigateToPageTools() => NavigateTo<PageToolsViewModel>();

        [RelayCommand]
        public void Navigate(string viewName)
        {
            switch (viewName)
            {
                case "Dashboard": NavigateToDashboard(); break;
                // The two print sections were merged into one; a stored or older
                // "PrintOperations" name still has to land somewhere real.
                case "PrintOperations":
                case "PrintManager": NavigateToPrintManager(); break;
                case "NumberedBooks": NavigateToNumberedBooks(); break;
                case "Performance": NavigateToPerformance(); break;
                case "Settings": NavigateToSettings(); break;
                case "ColorCalibration": NavigateToColorCalibration(); break;
                case "TemplateDesigner": NavigateToTemplateDesigner(); break;
                case "Imposition": NavigateToImposition(); break;
                case "PageTools": NavigateToPageTools(); break;
            }
        }

        [RelayCommand]
        public void OpenLogViewer()
        {
            using var scope = _scopeFactory.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<Apex.Core.Interfaces.ILoggerService>();
            var logReader = scope.ServiceProvider.GetRequiredService<Apex.Core.Interfaces.ILogReaderService>();
            var window = new Views.LogViewerWindow(logger, logReader);
            window.Show();
        }
    }
}
