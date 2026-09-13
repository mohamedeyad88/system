using Apex.Core.Interfaces;
using Apex.Services;
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
            // Single DB round-trip — eliminates concurrent DbContext access
            var s = await _settingsService.GetAllSettingsAsync();
            string Get(string key, string def) => s.TryGetValue(key, out var v) ? v : def;

            CompanyName      = Get("CompanyName",    "Apex Printing");
            CompanyAddress   = Get("CompanyAddress", "");
            CompanyPhone     = Get("CompanyPhone",   "");
            LogoPath         = Get("LogoPath",       "");
            BackupPath       = Get("BackupPath",     "");
            // Show the language the app is actually running in. This used to read the saved
            // "Language" key (default "en"), while startup always opened in Arabic and the
            // sidebar toggle saved nothing — so an Arabic session showed "English" here.
            SelectedLanguage = Services.LocalizationService.Instance.IsArabicActive ? "ar" : "en";
        }

        [RelayCommand]
        private async Task SaveSettings()
        {
            await _settingsService.SetValueAsync("CompanyName",    CompanyName);
            await _settingsService.SetValueAsync("CompanyAddress", CompanyAddress);
            await _settingsService.SetValueAsync("CompanyPhone",   CompanyPhone);
            await _settingsService.SetValueAsync("LogoPath",       LogoPath);

            await _settingsService.SetValueAsync("BackupPath", BackupPath);
            await _settingsService.SetValueAsync(Services.LocalizationService.LanguageSettingKey, SelectedLanguage);

            // FIX: Apply language change immediately
            Services.LocalizationService.Instance.SwitchLanguage(SelectedLanguage);

            // Notify user
            var message = Services.LocalizationService.Instance.GetString("SettingsSavedSuccessfully") + ". " +
                         Services.LocalizationService.Instance.GetString("SomeChangesMayRequireRestart");
            MessageBox.Show(message, Services.LocalizationService.Instance.GetString("Settings"), MessageBoxButton.OK, MessageBoxImage.Information);
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
            // WPF folder picker via OpenFileDialog hack (no external lib required)
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "اختر مجلد النسخ الاحتياطي",
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "اختر هذا المجلد",
                ValidateNames = false,
                InitialDirectory = string.IsNullOrEmpty(BackupPath)
                    ? System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments)
                    : BackupPath
            };
            if (dlg.ShowDialog() == true)
                BackupPath = System.IO.Path.GetDirectoryName(dlg.FileName) ?? string.Empty;
        }

        [RelayCommand]
        private void RunBackup()
        {
            var result = BackupService.Instance.RunBackup(BackupPath);
            MessageBox.Show(result.Message,
                result.Success ? "النسخ الاحتياطي" : "خطأ في النسخ الاحتياطي",
                MessageBoxButton.OK,
                result.Success ? MessageBoxImage.Information : MessageBoxImage.Error);
        }
    }
}
