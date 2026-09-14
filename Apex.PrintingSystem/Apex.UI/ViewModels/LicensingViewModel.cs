using System;
using System.Threading.Tasks;
using System.Windows;
using Apex.Licensing;
using Apex.UI.Services;
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
        [ObservableProperty] private string _deviceDisplayId = "";
        [ObservableProperty] private string _licenseStatusText = "";
        [ObservableProperty] private string _licenseTypeText = "";
        [ObservableProperty] private string _expiryText = "";
        [ObservableProperty] private bool _isLicenseValid = false;
        [ObservableProperty] private bool _isTrialActive = false;
        [ObservableProperty] private int _daysRemaining = 0;
        [ObservableProperty] private string _statusColor = StatusPalette.Error;

        // ── Online serial activation (buy now, no need to wait for trial to end) ──
        [ObservableProperty] private string _serialKey = "";
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsNotActivating))]
        private bool _isActivating = false;
        [ObservableProperty] private string _onlineStatus = "";
        [ObservableProperty] private string _onlineStatusColor = "#94A3B8";

        /// <summary>Inverse of <see cref="IsActivating"/> for enabling input controls.</summary>
        public bool IsNotActivating => !IsActivating;

        private readonly OnlineActivationService _onlineActivation = new();

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
                MessageBox.Show(L("Lic_DeviceCopied"), L("Lic_Copied"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { }
        }

        [RelayCommand]
        private void OpenPricing() =>
            WebLinks.OpenPricing(IsTrialActive ? "trial" : IsLicenseValid ? "renewal" : "licence-screen");

        [RelayCommand]
        private void OpenWhatsApp()
        {
            try { WhatsAppActivationService.OpenWhatsApp(DeviceDisplayId); }
            catch (Exception ex)
            {
                MessageBox.Show(Lf("Lic_WhatsAppError", ex.Message), L("Dlg_Error"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Activate immediately with a purchased serial (APX-XXXXX-XXXXX-XXXXX) without
        /// waiting for the trial to end. Sends the serial + this device id to the
        /// license server, installs the returned .apex, and restarts.
        /// </summary>
        [RelayCommand]
        private async Task ActivateWithSerialAsync()
        {
            if (IsActivating) return;

            if (string.IsNullOrWhiteSpace(SerialKey))
            {
                OnlineStatus = L("Act_EnterSerial");
                OnlineStatusColor = StatusPalette.Error;
                return;
            }

            IsActivating = true;
            OnlineStatus = L("Act_ActivatingOnline");
            OnlineStatusColor = "#94A3B8";
            try
            {
                var outcome = await _onlineActivation.ActivateAsync(SerialKey.Trim());
                if (outcome.Success)
                {
                    OnlineStatus = L("Act_ActivatedRestart");
                    OnlineStatusColor = StatusPalette.Done;
                    MessageBox.Show(
                        Lf("Lic_ActivatedMsg", outcome.Result?.Type,
                            outcome.Result?.ExpiresUtc?.Year >= 9999
                                ? L("Lic_Permanent")
                                : outcome.Result?.ExpiresUtc?.ToString("yyyy-MM-dd")),
                        L("Lic_ActivatedTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                    RestartApplication();
                }
                else
                {
                    OnlineStatus = $"❌ {outcome.Message}";
                    OnlineStatusColor = StatusPalette.Error;
                }
            }
            catch (Exception ex)
            {
                OnlineStatus = Lf("Act_UnexpectedError", ex.Message);
                OnlineStatusColor = StatusPalette.Error;
            }
            finally
            {
                IsActivating = false;
            }
        }

        [RelayCommand]
        private void ImportLicense()
        {
            var dlg = new OpenFileDialog
            {
                Title = L("Lic_ChooseFile"),
                Filter = "Apex License (*.apex)|*.apex|All Files (*.*)|*.*",
                Multiselect = false
            };
            if (dlg.ShowDialog() != true) return;

            var (success, result) = LicenseManager.InstallLicenseFile(dlg.FileName);
            if (success)
            {
                MessageBox.Show(
                    Lf("Lic_ActivatedMsg", result.Type, result.ExpiresUtc?.ToString("yyyy-MM-dd")),
                    L("Lic_ActivatedTitle"), MessageBoxButton.OK, MessageBoxImage.Information);

                RestartApplication();
            }
            else
            {
                MessageBox.Show(Lf("Lic_InstallFailed", result.ErrorMessage), L("Dlg_Error"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>Relaunch the app so the newly installed license is picked up at startup.</summary>
        private static void RestartApplication()
        {
            var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exe))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
            Application.Current.Shutdown();
        }

        [RelayCommand]
        private void RefreshStatus()
        {
            // The id the licence server knows this machine by — what support must be sent.
            var info = MachineIdentity.ToInfo(LicenseManager.GetLicensedDeviceId());
            DeviceDisplayId = info.DisplayId;

            var r = LicenseManager.Validate();
            IsLicenseValid = r.IsValid;
            IsTrialActive = r.IsValid && r.Type == LicenseType.Trial;
            DaysRemaining = r.DaysRemaining;

            LicenseTypeText = r.Type switch
            {
                LicenseType.Trial => L("Lic_TypeTrial"),
                LicenseType.Full => L("Lic_TypeFull"),
                LicenseType.Pro => L("Lic_TypePro"),
                _ => L("Lic_Unknown")
            };

            LicenseStatusText = r.Status switch
            {
                LicenseStatus.Valid when r.Type == LicenseType.Trial
                                   => Lf("Lic_ActiveRemaining", r.DaysRemaining),
                LicenseStatus.Valid => L("Lic_Active"),
                LicenseStatus.Expired => L("Lic_Expired"),
                LicenseStatus.Invalid => L("Lic_Invalid"),
                LicenseStatus.ClockTampered => L("Lic_ClockTamper"),
                LicenseStatus.HardwareMismatch => L("Lic_OtherDevice"),
                LicenseStatus.Corrupted => L("Lic_Corrupt"),
                _ => L("Lic_UnknownStatus")
            };

            ExpiryText = r.ExpiresUtc.HasValue
                ? (r.ExpiresUtc.Value.Year >= 9999 ? L("Lic_Permanent") : r.ExpiresUtc.Value.ToString("yyyy-MM-dd"))
                : "—";

            StatusColor = r.IsValid
                ? (r.Type == LicenseType.Trial ? StatusPalette.Warning : StatusPalette.Done)
                : StatusPalette.Error;
        }
    }
}
