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
        public bool IsDashboardActive       => CurrentViewModel is DashboardViewModel;
        public bool IsPrintManagerActive    => CurrentViewModel is PrintManagerViewModel;
        public bool IsDistributionActive    => CurrentViewModel is DistributionViewModel;
        public bool IsNumberedBooksActive   => CurrentViewModel is NumberingWizardViewModel;
        public bool IsPrintOperationsActive => CurrentViewModel is PrintOperationsViewModel;
        public bool IsPerformanceActive     => CurrentViewModel is SystemPerformanceViewModel;
        public bool IsQuotationActive       => CurrentViewModel is QuotationViewModel;
        public bool IsSettingsActive        => CurrentViewModel is SettingsViewModel;
        public bool IsLicensingActive       => CurrentViewModel is LicensingViewModel;
        public bool IsAnalyticsActive      => CurrentViewModel is AnalyticsDashboardViewModel;
        public bool IsUserManagementActive => CurrentViewModel is UserManagementViewModel;
        public bool IsReportsActive        => CurrentViewModel is ReportsViewModel;
        public bool IsLoadBalancerActive      => CurrentViewModel is LoadBalancerViewModel;
        public bool IsColorCalibrationActive  => CurrentViewModel is ColorCalibrationViewModel;
        public bool IsTemplateDesignerActive  => CurrentViewModel is TemplateDesignerViewModel;

        public MainViewModel(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        }

        partial void OnCurrentViewModelChanged(ViewModelBase? value)
        {
            if (value != null)
                _ = InitializeCurrentViewModelAsync(value);

            OnPropertyChanged(nameof(IsDashboardActive));
            OnPropertyChanged(nameof(IsPrintManagerActive));
            OnPropertyChanged(nameof(IsDistributionActive));
            OnPropertyChanged(nameof(IsNumberedBooksActive));
            OnPropertyChanged(nameof(IsPrintOperationsActive));
            OnPropertyChanged(nameof(IsPerformanceActive));
            OnPropertyChanged(nameof(IsQuotationActive));
            OnPropertyChanged(nameof(IsSettingsActive));
            OnPropertyChanged(nameof(IsLicensingActive));
            OnPropertyChanged(nameof(IsAnalyticsActive));
            OnPropertyChanged(nameof(IsUserManagementActive));
            OnPropertyChanged(nameof(IsReportsActive));
            OnPropertyChanged(nameof(IsLoadBalancerActive));
            OnPropertyChanged(nameof(IsColorCalibrationActive));
            OnPropertyChanged(nameof(IsTemplateDesignerActive));
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
        public void NavigateToPrinters() => NavigateTo<PrintOperationsViewModel>();

        [RelayCommand]
        public void NavigateToPrintManager() => NavigateTo<PrintManagerViewModel>();

        [RelayCommand]
        public void NavigateToNumberedBooks() => NavigateTo<NumberingWizardViewModel>();

        [RelayCommand]
        public void NavigateToPerformance() => NavigateTo<SystemPerformanceViewModel>();

        [RelayCommand]
        public void NavigateToDistribution() => NavigateTo<DistributionViewModel>();

        [RelayCommand]
        public void NavigateToQuotation() => NavigateTo<QuotationViewModel>();

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
        public void NavigateToAnalytics() => NavigateTo<AnalyticsDashboardViewModel>();

        [RelayCommand]
        public void NavigateToUserManagement() => NavigateTo<UserManagementViewModel>();

        [RelayCommand]
        public void NavigateToReports() => NavigateTo<ReportsViewModel>();

        [RelayCommand]
        public void NavigateToLoadBalancer() => NavigateTo<LoadBalancerViewModel>();

        [RelayCommand]
        public void NavigateToColorCalibration() => NavigateTo<ColorCalibrationViewModel>();

        [RelayCommand]
        public void NavigateToTemplateDesigner() => NavigateTo<TemplateDesignerViewModel>();

        [RelayCommand]
        public void Navigate(string viewName)
        {
            switch (viewName)
            {
                case "Dashboard":    NavigateToDashboard();   break;
                case "Printers":     NavigateToPrinters();    break;
                case "PrintManager": NavigateToPrintManager(); break;
                case "NumberedBooks": NavigateToNumberedBooks(); break;
                case "BatchPrint":   NavigateTo<BatchPrintViewModel>(); break;
                case "Distribution": NavigateTo<DistributionViewModel>(); break;
                case "Performance":  NavigateToPerformance(); break;
                case "Settings":     NavigateToSettings();    break;
                case "Quotation":    NavigateToQuotation();   break;
                case "Analytics":       NavigateToAnalytics();       break;
                case "UserManagement":  NavigateToUserManagement();  break;
                case "Reports":         NavigateToReports();         break;
                case "LoadBalancer":       NavigateToLoadBalancer();       break;
                case "ColorCalibration":  NavigateToColorCalibration();   break;
                case "TemplateDesigner":  NavigateToTemplateDesigner();   break;
            }
        }

        [RelayCommand]
        public void OpenLogViewer()
        {
            using var scope = _scopeFactory.CreateScope();
            var logger    = scope.ServiceProvider.GetRequiredService<Apex.Core.Interfaces.ILoggerService>();
            var logReader = scope.ServiceProvider.GetRequiredService<Apex.Core.Interfaces.ILogReaderService>();
            var window    = new Views.LogViewerWindow(logger, logReader);
            window.Show();
        }
    }
}
