using System;
using System.Windows;
using Apex.Licensing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace Apex.UI.ViewModels
{
    /// <summary>
    /// In-app license management page (navigated from sidebar).
    /// Shows current license status and allows activation.
    /// </summary>
    public partial class LicensingViewModel : ViewModelBase
    {
        [ObservableProperty] private string _deviceDisplayId   = "";
        [ObservableProperty] private string _licenseStatusText = "";
        [ObservableProperty] private string _licenseTypeText   = "";
        [ObservableProperty] private string _expiryText        = "";
        [ObservableProperty] private bool   _isLicenseValid    = false;
        [ObservableProperty] private bool   _isTrialActive     = false;
        [ObservableProperty] private int    _daysRemaining     = 0;
        [ObservableProperty] private string _statusColor       = "#EF4444";

        public LicensingViewModel()
        {
            RefreshStatus();
        }

        // ──────────────────────────────────────────────────────────────────
        //  Commands
        // ──────────────────────────────────────────────────────────────────

        [RelayCommand]
        private void CopyDeviceId()
        {
            try
            {
                Clipboard.SetText(DeviceDisplayId);
                MessageBox.Show("تم نسخ رقم الجهاز إلى الحافظة.", "تم النسخ",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { }
        }

        [RelayCommand]
        private void OpenWhatsApp()
        {
            try { WhatsAppActivationService.OpenWhatsApp(DeviceDisplayId); }
            catch (Exception ex)
            {
                MessageBox.Show($"تعذّر فتح واتساب:\n{ex.Message}", "خطأ",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        [RelayCommand]
        private void ImportLicense()
        {
            var dlg = new OpenFileDialog
            {
                Title       = "اختر ملف الترخيص",
                Filter      = "Apex License (*.apex)|*.apex|All Files (*.*)|*.*",
                Multiselect = false
            };
            if (dlg.ShowDialog() != true) return;

            var (success, result) = LicenseManager.InstallLicenseFile(dlg.FileName);
            if (success)
            {
                MessageBox.Show(
                    $"تم تفعيل البرنامج بنجاح!\n\nنوع الترخيص: {result.Type}\nينتهي في: {result.ExpiresUtc:yyyy-MM-dd}",
                    "تم التفعيل ✅", MessageBoxButton.OK, MessageBoxImage.Information);

                // Restart to pick up the new license
                var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exe))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
                Application.Current.Shutdown();
            }
            else
            {
                MessageBox.Show($"فشل تثبيت الترخيص:\n\n{result.ErrorMessage}", "خطأ",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void RefreshStatus()
        {
            var info   = LicenseManager.GetDeviceInfo();
            DeviceDisplayId = info.DisplayId;

            var r = LicenseManager.Validate();
            IsLicenseValid = r.IsValid;
            IsTrialActive  = r.IsValid && r.Type == LicenseType.Trial;
            DaysRemaining  = r.DaysRemaining;

            LicenseTypeText = r.Type switch
            {
                LicenseType.Trial => "فترة تجريبية",
                LicenseType.Full  => "ترخيص كامل",
                LicenseType.Pro   => "ترخيص احترافي",
                _                 => "غير معروف"
            };

            LicenseStatusText = r.Status switch
            {
                LicenseStatus.Valid when r.Type == LicenseType.Trial
                                   => $"✅ نشط – باقي {r.DaysRemaining} يوم",
                LicenseStatus.Valid => "✅ نشط",
                LicenseStatus.Expired         => "⏰ منتهي الصلاحية",
                LicenseStatus.Invalid         => "❌ غير صالح",
                LicenseStatus.ClockTampered   => "🔴 تلاعب بالتاريخ",
                LicenseStatus.HardwareMismatch => "💻 جهاز مختلف",
                LicenseStatus.Corrupted       => "⚠ بيانات تالفة",
                _                             => "❓ غير معروف"
            };

            ExpiryText = r.ExpiresUtc.HasValue
                ? (r.ExpiresUtc.Value.Year >= 9999 ? "دائم" : r.ExpiresUtc.Value.ToString("yyyy-MM-dd"))
                : "—";

            StatusColor = r.IsValid
                ? (r.Type == LicenseType.Trial ? "#F59E0B" : "#10B981")
                : "#EF4444";
        }
    }
}
