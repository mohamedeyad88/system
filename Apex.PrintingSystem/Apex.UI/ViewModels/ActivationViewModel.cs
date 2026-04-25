using System;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Apex.Licensing;
using Microsoft.Win32;

namespace Apex.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the Activation / Trial screen.
    /// Shown when the app cannot run (trial expired or license invalid).
    /// </summary>
    public partial class ActivationViewModel : ObservableObject
    {
        // ──────────────────────────────────────────────────────────────────
        //  Observable properties
        // ──────────────────────────────────────────────────────────────────

        [ObservableProperty] private string  _deviceDisplayId   = "";
        [ObservableProperty] private string  _statusMessage     = "";
        [ObservableProperty] private string  _statusDetail      = "";
        [ObservableProperty] private bool    _isTrialExpired    = false;
        [ObservableProperty] private int     _trialDaysRemaining = 0;
        [ObservableProperty] private bool    _showTrialInfo     = false;
        [ObservableProperty] private bool    _licenseInstalled  = false;

        /// <summary>Set by App.xaml.cs before showing the window.</summary>
        public Apex.Licensing.ValidationResult? ValidationResult { get; set; }

        // ──────────────────────────────────────────────────────────────────
        //  Constructor
        // ──────────────────────────────────────────────────────────────────

        public ActivationViewModel()
        {
            var info = LicenseManager.GetDeviceInfo();
            DeviceDisplayId = info.DisplayId;
        }

        /// <summary>Called after ValidationResult is set, to populate UI state.</summary>
        public void Initialize()
        {
            if (ValidationResult == null) return;

            var r = ValidationResult;
            IsTrialExpired      = !r.IsValid;
            TrialDaysRemaining  = r.DaysRemaining;
            ShowTrialInfo       = r.Type == LicenseType.Trial;
            StatusMessage       = BuildStatusMessage(r);
            StatusDetail        = r.ErrorMessage;
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
                MessageBox.Show(
                    "تم نسخ رقم الجهاز إلى الحافظة.",
                    "تم النسخ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch { }
        }

        [RelayCommand]
        private void OpenWhatsApp()
        {
            try
            {
                WhatsAppActivationService.OpenWhatsApp(DeviceDisplayId);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"تعذّر فتح واتساب:\n{ex.Message}",
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        [RelayCommand]
        private void BrowseLicenseFile()
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
                LicenseInstalled = true;
                MessageBox.Show(
                    $"تم تفعيل البرنامج بنجاح!\n\nنوع الترخيص: {result.Type}\n" +
                    $"ينتهي في: {result.ExpiresUtc:yyyy-MM-dd}",
                    "تم التفعيل ✅",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                // Restart the app to pick up the new license
                RestartApplication();
            }
            else
            {
                MessageBox.Show(
                    $"فشل تثبيت الترخيص:\n\n{result.ErrorMessage}",
                    "خطأ في الترخيص",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // ──────────────────────────────────────────────────────────────────
        //  Helpers
        // ──────────────────────────────────────────────────────────────────

        private static string BuildStatusMessage(Apex.Licensing.ValidationResult r) =>
            r.Status switch
            {
                LicenseStatus.Expired         => "⏰ انتهت الفترة التجريبية",
                LicenseStatus.Invalid         => "❌ ترخيص غير صالح",
                LicenseStatus.ClockTampered   => "🔴 تم اكتشاف تلاعب بالتاريخ",
                LicenseStatus.HardwareMismatch => "💻 الترخيص مقيد بجهاز آخر",
                LicenseStatus.Corrupted       => "⚠ بيانات الترخيص تالفة",
                LicenseStatus.Valid when r.Type == LicenseType.Trial
                                              => $"🕐 الفترة التجريبية – باقي {r.DaysRemaining} يوم",
                _                             => "❓ حالة غير معروفة"
            };

        private static void RestartApplication()
        {
            var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exe))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe)
                {
                    UseShellExecute = true
                });
            }
            Application.Current.Shutdown();
        }
    }
}
