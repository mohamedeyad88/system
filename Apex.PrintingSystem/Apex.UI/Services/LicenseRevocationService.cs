using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.UI.Services
{
    /// <summary>
    /// Soft (best-effort) license revocation. Asks the license server whether THIS
    /// device's Apex license has been revoked — e.g. after a refund the admin marks it
    /// so in the panel. The check is <b>fail-open</b>: any doubt (offline, timeout,
    /// server error, device not tracked) is treated as "not revoked", so a print shop
    /// with no internet is never locked out. Revocation only bites when the machine is
    /// online and the server answers explicitly "revoked".
    ///
    /// The server override env var (APEX_LICENSE_SERVER) matches OnlineActivationService.
    /// </summary>
    public sealed class LicenseRevocationService
    {
        private const string DefaultBaseUrl = "https://apexprint.me";

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

        private static string BaseUrl =>
            (Environment.GetEnvironmentVariable("APEX_LICENSE_SERVER") ?? DefaultBaseUrl).TrimEnd('/');

        /// <summary>
        /// True only when the server explicitly reports this device's license revoked.
        /// Never throws; returns false on any network/parse failure.
        /// </summary>
        public async Task<bool> IsRevokedAsync(string deviceId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) return false;
            try
            {
                var requestBody = JsonSerializer.Serialize(new { deviceId });
                using var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
                // ConfigureAwait(false): this is called sync-over-async from App startup
                // (GetAwaiter().GetResult() on the UI thread). Without it the continuation
                // is posted back to the blocked UI dispatcher and the whole app deadlocks
                // on the splash — the bounded timeout can't save it because the cancellation
                // completion needs that same blocked thread.
                var resp = await Http.PostAsync($"{BaseUrl}/api/apex/status", content, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return false;   // fail-open

                var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var parsed = JsonSerializer.Deserialize<StatusResponse>(
                    text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return parsed?.Revoked == true;
            }
            catch
            {
                // Offline / timeout / server error → keep the app running (fail-open).
                return false;
            }
        }

        private sealed class StatusResponse
        {
            public bool Revoked { get; set; }
            public bool Active { get; set; }
        }
    }
}
