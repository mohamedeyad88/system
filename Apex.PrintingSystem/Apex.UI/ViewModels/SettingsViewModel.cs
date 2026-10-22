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
            _ = CheckForUpdateSilentAsync();   // badge appears on its own if a newer build exists
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

        // ── Update check ──────────────────────────────────────────────────────
        /// <summary>The running build, e.g. "2.9.9".</summary>
        public string CurrentVersion =>
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

        [ObservableProperty] private bool _isCheckingUpdate;
        [ObservableProperty] private string _updateStatus = "";
        [ObservableProperty] private bool _updateAvailable;   // → the notification badge on the button
        [ObservableProperty] private string _latestVersion = "";
        private string _updateDownloadUrl = "";

        /// <summary>Silent check on opening Settings, so the badge shows up on its own when a
        /// newer build was published — without the operator having to press the button.</summary>
        private async Task CheckForUpdateSilentAsync()
        {
            try
            {
                var info = await new Services.UpdateCheckService().CheckAsync(CurrentVersion);
                if (info?.Available == true)
                {
                    UpdateAvailable = true;
                    LatestVersion = info.LatestVersion;
                    _updateDownloadUrl = info.DownloadUrl;
                    UpdateStatus = $"يوجد تحديث جديد: النسخة {info.LatestVersion} (نسختك {CurrentVersion}).";
                }
            }
            catch { /* best-effort; the manual button still works */ }
        }

        [RelayCommand]
        private async Task CheckForUpdate()
        {
            if (IsCheckingUpdate) return;
            IsCheckingUpdate = true;
            UpdateAvailable = false;
            UpdateStatus = "جارٍ البحث عن تحديث…";
            try
            {
                var info = await new Services.UpdateCheckService().CheckAsync(CurrentVersion);
                if (info == null)
                    UpdateStatus = "تعذّر الاتصال بالخادم. تأكد من الإنترنت وحاول تاني.";
                else if (info.Available)
                {
                    UpdateAvailable = true;
                    LatestVersion = info.LatestVersion;
                    _updateDownloadUrl = info.DownloadUrl;
                    UpdateStatus = $"يوجد تحديث جديد: النسخة {info.LatestVersion} (نسختك {CurrentVersion}).";
                }
                else
                    UpdateStatus = $"أنت على أحدث نسخة ({CurrentVersion}).";
            }
            catch { UpdateStatus = "حصل خطأ أثناء البحث عن التحديث."; }
            finally { IsCheckingUpdate = false; }
        }

        [RelayCommand]
        private void DownloadUpdate()
        {
            if (string.IsNullOrWhiteSpace(_updateDownloadUrl)) return;
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(_updateDownloadUrl) { UseShellExecute = true });
            }
            catch
            {
                MessageBox.Show("تعذّر فتح رابط التحميل.", "تحديث", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
