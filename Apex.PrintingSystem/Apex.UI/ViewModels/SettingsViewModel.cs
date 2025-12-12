using Apex.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;
using System.Windows;

namespace Apex.UI.ViewModels
{
    public partial class SettingsViewModel : ViewModelBase
    {
        private readonly ISettingsService _settingsService;

        // Company Info
        [ObservableProperty] private string _companyName = string.Empty;
        [ObservableProperty] private string _companyAddress = string.Empty;
        [ObservableProperty] private string _companyPhone = string.Empty;
        [ObservableProperty] private string _logoPath = string.Empty;

        // Financials
        [ObservableProperty] private string _invoiceHeader = string.Empty;
        [ObservableProperty] private string _invoiceFooter = string.Empty;
        [ObservableProperty] private decimal _defaultPricePerPage;
        [ObservableProperty] private decimal _defaultCoverPrice;

        // System
        [ObservableProperty] private string _backupPath = string.Empty;
        [ObservableProperty] private string _selectedLanguage = "en"; // "en" or "ar"

        public SettingsViewModel(ISettingsService settingsService)
        {
            _settingsService = settingsService ?? throw new System.ArgumentNullException(nameof(settingsService));
        }

        public override async Task InitializeAsync()
        {
            await LoadSettings();
        }

        [RelayCommand]
        private async Task LoadSettings()
        {
            CompanyName = await _settingsService.GetValueAsync("CompanyName", "Apex Printing");
            CompanyAddress = await _settingsService.GetValueAsync("CompanyAddress", "");
            CompanyPhone = await _settingsService.GetValueAsync("CompanyPhone", "");
            LogoPath = await _settingsService.GetValueAsync("LogoPath", "");

            InvoiceHeader = await _settingsService.GetValueAsync("InvoiceHeader", "");
            InvoiceFooter = await _settingsService.GetValueAsync("InvoiceFooter", "Thank you for your business!");
            
            var priceStr = await _settingsService.GetValueAsync("PricePerPage", "0.50");
            decimal.TryParse(priceStr, out var price);
            DefaultPricePerPage = price;

            var coverStr = await _settingsService.GetValueAsync("CoverPrice", "5.00");
            decimal.TryParse(coverStr, out var cover);
            DefaultCoverPrice = cover;

            BackupPath = await _settingsService.GetValueAsync("BackupPath", "");
            SelectedLanguage = await _settingsService.GetValueAsync("Language", "en");
        }

        [RelayCommand]
        private async Task SaveSettings()
        {
            await _settingsService.SetValueAsync("CompanyName", CompanyName);
            await _settingsService.SetValueAsync("CompanyAddress", CompanyAddress);
            await _settingsService.SetValueAsync("CompanyPhone", CompanyPhone);
            await _settingsService.SetValueAsync("LogoPath", LogoPath);

            await _settingsService.SetValueAsync("InvoiceHeader", InvoiceHeader);
            await _settingsService.SetValueAsync("InvoiceFooter", InvoiceFooter);
            await _settingsService.SetValueAsync("PricePerPage", DefaultPricePerPage.ToString());
            await _settingsService.SetValueAsync("CoverPrice", DefaultCoverPrice.ToString());

            await _settingsService.SetValueAsync("BackupPath", BackupPath);
            await _settingsService.SetValueAsync("Language", SelectedLanguage);

            // Notify user
            var message = Services.LocalizationService.Instance.GetString("SettingsSavedSuccessfully") + ". " + 
                         Services.LocalizationService.Instance.GetString("SomeChangesMayRequireRestart");
            MessageBox.Show(message, Services.LocalizationService.Instance.GetString("Settings"), MessageBoxButton.OK, MessageBoxImage.Information);
            
            // Trigger Language Change if needed
            // LocalizationService.SetLanguage(SelectedLanguage); // Assuming static or injected
        }

        [RelayCommand]
        private void BrowseLogo()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Images|*.png;*.jpg;*.bmp" };
            if (dialog.ShowDialog() == true)
            {
                LogoPath = dialog.FileName;
            }
        }

        [RelayCommand]
        private void BrowseBackupPath()
        {
            // Folder picker is tricky in WPF without external libs, using OpenFileDialog with CheckFileExists=false or just TextBox
            // For simplicity, we'll assume user types it or we use a hack with OpenFileDialog
            var dialog = new Microsoft.Win32.SaveFileDialog { Title = "Select Backup Location", FileName = "Select Folder" };
            if (dialog.ShowDialog() == true)
            {
                BackupPath = System.IO.Path.GetDirectoryName(dialog.FileName) ?? string.Empty;
            }
        }
    }
}
