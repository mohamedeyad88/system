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

        public MainViewModel(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _ = CheckForUpdatesAsync();   // fire-and-forget; never blocks startup
        }

        // ── App update notice (server-driven) ─────────────────────────────────
        [ObservableProperty] private bool _updateAvailable;
        [ObservableProperty] private string _updateVersion = "";
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
