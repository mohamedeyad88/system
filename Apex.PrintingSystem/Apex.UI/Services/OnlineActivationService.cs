using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Apex.Licensing;

namespace Apex.UI.Services
{
    /// <summary>
    /// Online activation: sends the purchased serial + this machine's Device ID to the
    /// license server, which binds the serial to the device and returns a signed
    /// <see cref="SignedLicense"/>. The license is then verified locally and installed.
    ///
    /// The server URL can be overridden with the APEX_LICENSE_SERVER environment
    /// variable; otherwise the compiled-in default is used.
    /// </summary>
    public sealed class OnlineActivationService
    {
        // Production license/activation server. Override at runtime with APEX_LICENSE_SERVER.
        private const string DefaultBaseUrl = "https://apexprint.me";

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

        private static string BaseUrl =>
            (Environment.GetEnvironmentVariable("APEX_LICENSE_SERVER") ?? DefaultBaseUrl)
                .TrimEnd('/');

        public sealed record ActivationOutcome(bool Success, string Message, ValidationResult? Result);

        /// <summary>
        /// Activates online. Returns success + an Arabic message, and installs the
        /// license on success. Never throws for expected failures (network/HTTP/serial).
        /// </summary>
        public async Task<ActivationOutcome> ActivateAsync(string serial, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(serial))
                return new ActivationOutcome(false, "يرجى إدخال رقم السيريال.", null);

            // Re-activating a machine that already holds a license keeps the id the server
            // bound it under, so it stays idempotent instead of spending a device change.
            var info = MachineIdentity.ToInfo(LicenseManager.GetLicensedDeviceId());
            var url = $"{BaseUrl}/api/apex/activate";
            var requestBody = JsonSerializer.Serialize(new
            {
                serial = serial.Trim(),
                deviceId = info.DeviceId,
                deviceDisplayId = info.DisplayId
            });

            HttpResponseMessage resp;
            string text;
            try
            {
                using var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
                resp = await Http.PostAsync(url, content, ct);
                text = await resp.Content.ReadAsStringAsync(ct);
            }
            catch (TaskCanceledException)
            {
                return new ActivationOutcome(false,
                    "انتهت مهلة الاتصال بخادم التفعيل. تحقّق من الإنترنت وحاول مرة أخرى.", null);
            }
            catch (HttpRequestException)
            {
                return new ActivationOutcome(false,
                    "تعذّر الاتصال بخادم التفعيل. تأكد من اتصال الإنترنت أو استخدم التفعيل عبر واتساب.", null);
            }

            ActivationResponse? parsed = null;
            try { parsed = JsonSerializer.Deserialize<ActivationResponse>(text, JsonOpts); }
            catch { /* fall through to generic error below */ }

            if (!resp.IsSuccessStatusCode || parsed is null || !parsed.Success)
            {
                var serverError = parsed?.Error;
                return new ActivationOutcome(false,
                    string.IsNullOrWhiteSpace(serverError)
                        ? $"فشل التفعيل (رمز {(int)resp.StatusCode})."
                        : serverError,
                    null);
            }

            if (parsed.License is null)
                return new ActivationOutcome(false, "استجابة الخادم لا تحتوي على ملف ترخيص.", null);

            var signed = new SignedLicense
            {
                Payload = parsed.License.Payload,
                Signature = parsed.License.Signature,
                SchemaVersion = parsed.License.SchemaVersion
            };

            var (installed, result) = LicenseManager.InstallSignedLicense(signed);
            return installed
                ? new ActivationOutcome(true, "تم التفعيل بنجاح.", result)
                : new ActivationOutcome(false, result.ErrorMessage, result);
        }

        // ── Response DTOs ─────────────────────────────────────────────────────

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private sealed class ActivationResponse
        {
            public bool Success { get; set; }
            public string? Error { get; set; }
            public LicenseDto? License { get; set; }
        }

        private sealed class LicenseDto
        {
            public string Payload { get; set; } = "";
            public string Signature { get; set; } = "";
            public string SchemaVersion { get; set; } = "1";
        }
    }
}
