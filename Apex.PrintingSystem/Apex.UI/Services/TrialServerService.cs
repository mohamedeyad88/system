using System;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.UI.Services
{
    /// <summary>
    /// Server-authoritative trial clock. On startup (when online) the app claims/refreshes
    /// this device's trial on the license server and gets back the canonical start date, so
    /// reinstalling or clearing local files can no longer reset the 7-day trial.
    ///
    /// <b>Fail-open / offline-first:</b> any doubt (offline, timeout, server error, no record)
    /// returns null, and the caller keeps using the local trial clock — a print shop with no
    /// internet is never affected or locked out. The server can only ever make the trial start
    /// EARLIER, never later, so this can correct a reset but can never extend the trial.
    ///
    /// The server override env var (APEX_LICENSE_SERVER) matches the other online services.
    /// </summary>
    public sealed class TrialServerService
    {
        private const string DefaultBaseUrl = "https://apexprint.me";

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

        private static string BaseUrl =>
            (Environment.GetEnvironmentVariable("APEX_LICENSE_SERVER") ?? DefaultBaseUrl).TrimEnd('/');

        /// <summary>
        /// Claims/refreshes the trial for <paramref name="deviceId"/>, reporting the local
        /// start so an honest older trial is preserved. Returns the server-authoritative start
        /// (UTC), or null on any failure. Never throws.
        /// </summary>
        public async Task<DateTime?> ClaimAsync(string deviceId, DateTime localStartUtc, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) return null;
            try
            {
                var requestBody = JsonSerializer.Serialize(new
                {
                    deviceId,
                    startedAt = localStartUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)
                });
                using var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

                // ConfigureAwait(false): called sync-over-async from App startup, mirroring
                // LicenseRevocationService — without it the continuation deadlocks the splash.
                var resp = await Http.PostAsync($"{BaseUrl}/api/apex/trial", content, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return null;   // fail-open

                var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var parsed = JsonSerializer.Deserialize<TrialResponse>(
                    text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (string.IsNullOrWhiteSpace(parsed?.StartUtc)) return null;
                if (DateTime.TryParse(parsed!.StartUtc, CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt))
                    return dt.ToUniversalTime();

                return null;
            }
            catch
            {
                // Offline / timeout / server error → fall back to the local clock.
                return null;
            }
        }

        private sealed class TrialResponse
        {
            public string? StartUtc { get; set; }
            public int DaysRemaining { get; set; }
            public bool Expired { get; set; }
        }
    }
}
