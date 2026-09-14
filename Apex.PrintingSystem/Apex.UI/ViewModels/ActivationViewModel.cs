using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Apex.Licensing;
using Apex.UI.Services;
using Microsoft.Win32;

namespace Apex.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the Activation / Trial screen.
    /// Shown when the app cannot run (trial expired or license invalid).
    /// </summary>
    public partial class ActivationViewModel : ViewModelBase
    {
        // ──────────────────────────────────────────────────────────────────
        //  Observable properties
        // ──────────────────────────────────────────────────────────────────

        [ObservableProperty] private string _deviceDisplayId = "";
        [ObservableProperty] private string _statusMessage = "";
        [ObservableProperty] private string _statusDetail = "";
        [ObservableProperty] private bool _isTrialExpired = false;
        [ObservableProperty] private int _trialDaysRemaining = 0;
        [ObservableProperty] private bool _showTrialInfo = false;
        [ObservableProperty] private bool _licenseInstalled = false;

        [ObservableProperty] private string _serialKey = "";
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsNotActivating))]
        [NotifyPropertyChangedFor(nameof(CanActivate))]
        private bool _isActivating = false;
        [ObservableProperty] private string _onlineStatus = "";
        [ObservableProperty] private string _onlineStatusColor = "#94A3B8";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanActivate))]
        private bool _isEulaAccepted = false;

        /// <summary>Inverse of <see cref="IsActivating"/> for enabling input controls.</summary>
        public bool IsNotActivating => !IsActivating;

        /// <summary>Activation is allowed only after the EULA is accepted and no activation is running.</summary>
        public bool CanActivate => IsEulaAccepted && IsNotActivating;

        private readonly OnlineActivationService _onlineActivation = new();

        /// <summary>Set by App.xaml.cs before showing the window.</summary>
        public Apex.Licensing.ValidationResult? ValidationResult { get; set; }

        // ──────────────────────────────────────────────────────────────────
        //  Constructor
        // ──────────────────────────────────────────────────────────────────

        public ActivationViewModel()
        {
            // A licence that no longer validates (expired, revoked) may still name the id the
            // server bound; support needs that one, not a new one.
            var info = MachineIdentity.ToInfo(LicenseManager.GetLicensedDeviceId());
            DeviceDisplayId = info.DisplayId;
        }

        /// <summary>Called after ValidationResult is set, to populate UI state.</summary>
        public void Initialize()
        {
            if (ValidationResult == null) return;

            var r = ValidationResult;
            IsTrialExpired = !r.IsValid;
            TrialDaysRemaining = r.DaysRemaining;
            ShowTrialInfo = r.Type == LicenseType.Trial;
            StatusMessage = BuildStatusMessage(r);
            StatusDetail = r.ErrorMessage;
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
                    L("Lic_DeviceCopied"),
                    L("Lic_Copied"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch { }
        }

        [RelayCommand]
        private void ShowEula()
        {
            var win = new Views.Dialogs.EulaWindow { Owner = Application.Current?.MainWindow };
            win.ShowDialog();
        }

        [RelayCommand]
        private async Task ActivateOnlineAsync()
        {
            if (IsActivating) return;

            if (!IsEulaAccepted)
            {
                OnlineStatus = L("Eula_MustAccept");
                OnlineStatusColor = StatusPalette.Error;
                return;
            }

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
                var outcome = await _onlineActivation.ActivateAsync(SerialKey);
                if (outcome.Success)
                {
                    LicenseInstalled = true;
                    OnlineStatus = L("Act_ActivatedRestart");
                    OnlineStatusColor = StatusPalette.Done;
                    MessageBox.Show(
                        Lf("Lic_ActivatedMsg", outcome.Result?.Type,
                            outcome.Result?.ExpiresUtc?.Year == 9999 ? L("Lic_Permanent") : outcome.Result?.ExpiresUtc?.ToString("yyyy-MM-dd")),
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

        /// <summary>The screen says "enter the serial you received after purchase" — this is where to purchase.</summary>
        [RelayCommand]
        private void OpenPricing() => WebLinks.OpenPricing(IsTrialExpired ? "trial-ended" : "activation");

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
                    Lf("Lic_WhatsAppError", ex.Message),
                    L("Dlg_Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        [RelayCommand]
        private void BrowseLicenseFile()
        {
            if (!IsEulaAccepted)
            {
                MessageBox.Show(L("Eula_MustAccept"), L("Dlg_Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

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
                LicenseInstalled = true;
                MessageBox.Show(
                    Lf("Lic_ActivatedMsg", result.Type, result.ExpiresUtc?.ToString("yyyy-MM-dd")),
                    L("Lic_ActivatedTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                // Restart the app to pick up the new license
                RestartApplication();
            }
            else
            {
                MessageBox.Show(
                    Lf("Lic_InstallFailed", result.ErrorMessage),
                    L("Lic_ErrorTitle"),
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
                LicenseStatus.Expired => L("Act_TrialEnded"),
                LicenseStatus.Invalid => L("Act_InvalidLicense"),
                LicenseStatus.ClockTampered => L("Act_ClockTamper"),
                LicenseStatus.HardwareMismatch => L("Act_OtherDevice"),
                LicenseStatus.Corrupted => L("Act_CorruptData"),
                LicenseStatus.Valid when r.Type == LicenseType.Trial
                                              => Lf("Act_TrialRemaining", r.DaysRemaining),
                _ => L("Act_UnknownStatus")
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
