using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Apex.Licensing
{
    /// <summary>
    /// Main entry-point for all licensing checks in the application.
    ///
    /// Priority order:
    ///   1. Signed license file (*.apex) → Full/Pro activation
    ///   2. Trial period (5 days)        → Limited run
    ///   3. Everything else              → Activation required
    /// </summary>
    public static class LicenseManager
    {
        // ──────────────────────────────────────────────────────────────────
        //  Constants
        // ──────────────────────────────────────────────────────────────────

        private const string AppFolder      = "ApexPrintingSystem";
        private const string LicenseFile    = "license.apex";

        private static string LicensePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppFolder, LicenseFile);

        // ──────────────────────────────────────────────────────────────────
        //  Public API
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Validates licensing state. Call once at startup.
        ///
        /// Returns <see cref="ValidationResult.IsValid"/> = true if the app may run.
        /// </summary>
        public static ValidationResult Validate()
        {
            // ── 1. Try signed license file ──
            if (File.Exists(LicensePath))
            {
                var result = ValidateLicenseFile(LicensePath);
                if (result.IsValid)
                    return result;

                // License file exists but is invalid – fall through to trial
                // (don't block if someone put a corrupt file there by accident)
            }

            // ── 2. Trial mode ──
            return TrialManager.CheckTrial();
        }

        /// <summary>
        /// Installs a license file from any path (e.g., from an Open File dialog).
        /// Returns (true, result) if the license is valid for this machine.
        /// Returns (false, result) if invalid, with an error message.
        /// </summary>
        public static (bool Success, ValidationResult Result) InstallLicenseFile(string sourcePath)
        {
            try
            {
                var result = ValidateLicenseFile(sourcePath);
                if (!result.IsValid)
                    return (false, result);

                // Copy to canonical location
                var dir = Path.GetDirectoryName(LicensePath)!;
                Directory.CreateDirectory(dir);
                File.Copy(sourcePath, LicensePath, overwrite: true);

                return (true, result);
            }
            catch (Exception ex)
            {
                return (false, new ValidationResult
                {
                    IsValid      = false,
                    Status       = LicenseStatus.Invalid,
                    ErrorMessage = $"فشل تثبيت الترخيص: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Returns device info for display in the activation UI.
        /// </summary>
        public static DeviceIdentityInfo GetDeviceInfo() =>
            MachineIdentity.GetDeviceInfo();

        /// <summary>
        /// Returns true if a valid full license is already installed.
        /// </summary>
        public static bool HasFullLicense()
        {
            if (!File.Exists(LicensePath)) return false;
            var r = ValidateLicenseFile(LicensePath);
            return r.IsValid && r.Type != LicenseType.Trial;
        }

        // ──────────────────────────────────────────────────────────────────
        //  License file validation
        // ──────────────────────────────────────────────────────────────────

        private static ValidationResult ValidateLicenseFile(string path)
        {
            try
            {
                var json         = File.ReadAllText(path, Encoding.UTF8);
                var signedLicense = JsonSerializer.Deserialize<SignedLicense>(json);

                if (signedLicense == null)
                    return Error(LicenseStatus.Corrupted, "ملف الترخيص تالف.");

                var (isValid, payload) = LicenseCrypto.VerifyLicense(signedLicense);

                if (!isValid || payload == null)
                    return Error(LicenseStatus.Invalid, "التوقيع الرقمي غير صالح.");

                // Check expiry
                var now = DateTime.UtcNow;
                if (now > payload.ExpiresUtc)
                    return Error(LicenseStatus.Expired,
                        $"انتهت صلاحية الترخيص في {payload.ExpiresUtc:yyyy-MM-dd}.");

                // Check hardware binding
                var currentDevice = MachineIdentity.GetDeviceId();
                if (!string.IsNullOrEmpty(payload.DeviceId) &&
                    payload.DeviceId != currentDevice)
                    return Error(LicenseStatus.HardwareMismatch,
                        "الترخيص مقيد بجهاز آخر.");

                int days = (int)(payload.ExpiresUtc - now).TotalDays;
                return new ValidationResult
                {
                    IsValid       = true,
                    Status        = LicenseStatus.Valid,
                    Type          = payload.Type,
                    DaysRemaining = days,
                    ExpiresUtc    = payload.ExpiresUtc
                };
            }
            catch (Exception ex)
            {
                return Error(LicenseStatus.Corrupted, $"خطأ في قراءة الترخيص: {ex.Message}");
            }
        }

        private static ValidationResult Error(LicenseStatus status, string msg) =>
            new() { IsValid = false, Status = status, ErrorMessage = msg };
    }
}
