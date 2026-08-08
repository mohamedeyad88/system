using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.UI.Services
{
    /// <summary>Result of an update check.</summary>
    public sealed record UpdateInfo(
        bool Available, string LatestVersion, string CurrentVersion, string DownloadUrl, string Notes);

    /// <summary>
    /// Asks the Apex server for the latest published version and compares it to the running
    /// build. On a newer version it returns an <see cref="UpdateInfo"/> the UI shows as a
    /// "تحديث متاح" banner with a download link. Best-effort: any failure returns null so the
    /// app never blocks on the network.
    /// </summary>
    public class UpdateCheckService
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
        private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

        public const string DefaultEndpoint = "https://apexprint.me/api/apex/latest-version";

        private readonly string _endpoint;
        public UpdateCheckService(string? endpoint = null) => _endpoint = endpoint ?? DefaultEndpoint;

        public async Task<UpdateInfo?> CheckAsync(string currentVersion, CancellationToken ct = default)
        {
            try
            {
                string json = await Http.GetStringAsync(_endpoint, ct).ConfigureAwait(false);
                var dto = JsonSerializer.Deserialize<LatestVersionDto>(json, JsonOpts);
                if (dto == null || string.IsNullOrWhiteSpace(dto.Version)) return null;

                bool newer = CompareVersions(dto.Version, currentVersion) > 0;
                return new UpdateInfo(newer, dto.Version, currentVersion, dto.DownloadUrl ?? "", dto.Notes ?? "");
            }
            catch
            {
                return null; // offline / server error — never block the app
            }
        }

        /// <summary>Compares dotted numeric versions ("2.3.0" vs "2.2.1"). &gt;0 if a is newer.</summary>
        public static int CompareVersions(string a, string b)
        {
            int[] pa = Parse(a), pb = Parse(b);
            int len = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < len; i++)
            {
                int va = i < pa.Length ? pa[i] : 0;
                int vb = i < pb.Length ? pb[i] : 0;
                if (va != vb) return va.CompareTo(vb);
            }
            return 0;
        }

        private static int[] Parse(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return new[] { 0 };
            // Keep the leading numeric.dotted part, drop any suffix like "-beta".
            int cut = v.IndexOfAny(new[] { '-', '+', ' ' });
            if (cut >= 0) v = v.Substring(0, cut);
            var parts = v.Split('.');
            var nums = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                nums[i] = int.TryParse(parts[i].Trim(), out int n) ? n : 0;
            return nums;
        }

        private sealed class LatestVersionDto
        {
            public string? Version { get; set; }
            public string? DownloadUrl { get; set; }
            public string? Notes { get; set; }
        }
    }
}
