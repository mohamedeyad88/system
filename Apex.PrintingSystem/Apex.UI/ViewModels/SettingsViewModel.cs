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

        // Company name, address, phone and logo were collected here and written to the
        // database, and nothing ever read them back: they reached no printed sheet, no
        // report, no invoice. Asking a shop to fill them in implied otherwise, so the
        // fields are gone until something prints them.

        // System
        [ObservableProperty] private string _backupPath = string.Empty;
        [ObservableProperty] private string _selectedLanguage = "en"; // "en" or "ar"

        /// <summary>
        /// "light" or "dark". Unlike the language it takes effect the moment it is
        /// picked — the point of choosing a theme is to see it — and is remembered
        /// straight away rather than waiting for "حفظ".
        /// </summary>
        [ObservableProperty] private string _selectedTheme = "light";

        private bool _loadingTheme;

        partial void OnSelectedThemeChanged(string value)
        {
            if (_loadingTheme) return;
            Services.ThemeService.Instance.Apply(value);
            _ = _settingsService.SetValueAsync(Services.ThemeService.ThemeSettingKey, value);
        }

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

            BackupPath       = Get("BackupPath",     "");
            // Show the language the app is actually running in. This used to read the saved
            // "Language" key (default "en"), while startup always opened in Arabic and the
            // sidebar toggle saved nothing — so an Arabic session showed "English" here.
            SelectedLanguage = Services.LocalizationService.Instance.IsArabicActive ? "ar" : "en";

            // Show the theme that is actually on screen, without re-applying it.
            _loadingTheme = true;
            SelectedTheme = Services.ThemeService.Instance.IsDark ? "dark" : "light";
            _loadingTheme = false;
        }

        [RelayCommand]
        private async Task SaveSettings()
        {
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
