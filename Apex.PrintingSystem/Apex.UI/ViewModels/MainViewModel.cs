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

        public MainViewModel(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            // Default view
            NavigateTo<DashboardViewModel>();
        }

        partial void OnCurrentViewModelChanged(ViewModelBase? value)
        {
            // When the current ViewModel changes, initialize it asynchronously
            if (value != null)
            {
                _ = InitializeCurrentViewModelAsync(value);
            }
        }

        private async Task InitializeCurrentViewModelAsync(ViewModelBase viewModel)
        {
            try
            {
                await viewModel.InitializeAsync();
            }
            catch (Exception ex)
            {
                System.IO.File.AppendAllText("viewmodel_error.log", $"[{DateTime.Now}] Error initializing {viewModel.GetType().Name}:\n{ex}\n\n");
            }
        }

        private void NavigateTo<T>() where T : ViewModelBase
        {
            try 
            {
                // Dispose previous scope to free resources (DbContext, etc.)
                _currentScope?.Dispose();
                
                // Create new scope for the new ViewModel
                _currentScope = _scopeFactory.CreateScope();
                
                // Resolve ViewModel within the new scope
                CurrentViewModel = _currentScope.ServiceProvider.GetRequiredService<T>();
            }
            catch (Exception ex)
            {
                System.IO.File.AppendAllText("navigation_error.log", $"[{DateTime.Now}] Error navigating to {typeof(T).Name}:\n{ex}\n\n");
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
        public void SwitchLanguage(string cultureCode)
        {
            Services.LocalizationService.Instance.SwitchLanguage(cultureCode);
        }

        /// <summary>
        /// Parameterized navigation command for Quick Actions and other dynamic navigation
        /// </summary>
        [RelayCommand]
        public void Navigate(string viewName)
        {
            switch (viewName)
            {
                case "Dashboard":
                    NavigateToDashboard();
                    break;
                case "Printers":
                    NavigateToPrinters();
                    break;
                case "PrintManager":
                    NavigateToPrintManager();
                    break;
                case "NumberedBooks":
                    NavigateToNumberedBooks();
                    break;
                case "BatchPrint":
                    NavigateTo<BatchPrintViewModel>();
                    break;
                case "Distribution":
                    NavigateTo<DistributionViewModel>();
                    break;
                case "Performance":
                    NavigateToPerformance();
                    break;
                case "Settings":
                    NavigateTo<SettingsViewModel>();
                    break;
                default:
                    // Unknown view, stay on current
                    break;
            }
        }

        [RelayCommand]
        public void OpenLogViewer()
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var logger = scope.ServiceProvider.GetRequiredService<Apex.Core.Interfaces.ILoggerService>();
                var logReader = scope.ServiceProvider.GetRequiredService<Apex.Core.Interfaces.ILogReaderService>();
                var window = new Views.LogViewerWindow(logger, logReader);
                window.Show();
            }
        }
    }
}
