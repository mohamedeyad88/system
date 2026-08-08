using System;
using System.IO;
using System.Text;

namespace Apex.UI.Services
{
    /// <summary>
    /// Handles 7-day trial enforcement with local storage (no network).
    /// Stores start/last-check timestamps in AppData with simple salted Base64.
    /// </summary>
    public static class TrialLicenseService
    {
        private const int TrialDays = 7;
        private const string Salt = "ApexTrialSalt2025";

        private static readonly string TrialFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ApexPrintingSystem",
            "trial.dat");

        private static readonly TimeSpan BackdateTolerance = TimeSpan.FromDays(1);

        public static bool EnsureTrialValid(out string errorMessage)
        {
            errorMessage = string.Empty;
            try
            {
                if (!File.Exists(TrialFilePath))
                {
                    SaveState(DateTime.UtcNow, DateTime.UtcNow);
                    return true;
                }

                var state = LoadState();
                if (state == null)
                {
                    errorMessage = "Trial data corrupted.";
                    return false;
                }

                var (startUtc, lastUtc) = state.Value;
                var now = DateTime.UtcNow;

                // Detect clock tampering (backdating)
                if (now + BackdateTolerance < lastUtc || now < startUtc)
                {
                    errorMessage = "Trial expired (system time change detected).";
                    return false;
                }

                var daysUsed = (now - startUtc).TotalDays;
                if (daysUsed > TrialDays)
                {
                    errorMessage = $"Trial period of {TrialDays} days has ended.";
                    return false;
                }

                // Update last check (monotonic)
                var newLast = now > lastUtc ? now : lastUtc;
                SaveState(startUtc, newLast);
                return true;
            }
            catch
            {
                errorMessage = "Unable to verify trial period.";
                return false;
            }
        }

        private static void SaveState(DateTime startUtc, DateTime lastUtc)
        {
            var dir = Path.GetDirectoryName(TrialFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var payload = $"{startUtc.Ticks}|{lastUtc.Ticks}|{Salt}";
            var bytes = Encoding.UTF8.GetBytes(payload);
            var encoded = Convert.ToBase64String(bytes);
            File.WriteAllText(TrialFilePath, encoded);

        }

        private static (DateTime start, DateTime last)? LoadState()
        {
            try
            {
                var encoded = File.ReadAllText(TrialFilePath);
                var bytes = Convert.FromBase64String(encoded);
                var payload = Encoding.UTF8.GetString(bytes);
                var parts = payload.Split('|');
                if (parts.Length != 3 || parts[2] != Salt)
                    return null;

                if (long.TryParse(parts[0], out var startTicks) &&
                    long.TryParse(parts[1], out var lastTicks))
                {
                    var start = new DateTime(startTicks, DateTimeKind.Utc);
                    var last = new DateTime(lastTicks, DateTimeKind.Utc);
                    return (start, last);
                }

                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}

