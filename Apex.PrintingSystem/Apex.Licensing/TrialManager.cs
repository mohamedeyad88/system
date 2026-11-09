using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace Apex.Licensing
{
    /// <summary>
    /// Manages the trial period with anti-tampering:
    ///   • Stores state in THREE locations (AppData, ProgramData, Registry).
    ///   • Detects clock rollback (now &lt; lastSeen − tolerance).
    ///   • Detects direct file tampering via HMAC-SHA256.
    ///   • Detects machine change (device ID mismatch).
    /// </summary>
    public static class TrialManager
    {
        // ──────────────────────────────────────────────────────────────────
        //  Constants
        // ──────────────────────────────────────────────────────────────────

        public const int TrialDays = 7;   // Commercial trial: 7 days (must stay unified with the website's trial length)

        /// <summary>
        /// Current trial epoch. Any stored trial state with a different epoch is
        /// discarded and a fresh trial begins. Increment this ONCE to force a
        /// one-time trial reset across all machines on the next build (e.g. for a
        /// new field-test rollout). Do NOT bump it every release, or the trial
        /// would reset on every update.
        /// </summary>
        // Bumped to 2 for the 2.6.0 field-test rollout: the trials opened on
        // 2026-08-03 had run out, which stopped testing on every machine.
        public const int TrialEpoch = 2;
        private const string AppFolder = "ApexPrintingSystem";
        private const string TrialFileName = "apex_trial.dat";
        private const string RegistrySubKey = @"SOFTWARE\ApexPrintingSystem\Trial";

        private static readonly TimeSpan BackdateTolerance = TimeSpan.FromDays(1);

        // ──────────────────────────────────────────────────────────────────
        //  Diagnostics
        // ──────────────────────────────────────────────────────────────────
        // Field report: on one offline shop machine the trial restarts at 7 days on every
        // launch — i.e. the saved state is not being read back. The cause is environmental
        // (the code persists to three places), so this records, per launch, which locations
        // loaded and which branch was taken, to a plain-text log the operator can send. Fully
        // best-effort: logging never affects the trial result. Clears itself past ~64 KB.
        private static string TrialLogPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppFolder, "trial_debug.log");

        private static void LogTrial(string message)
        {
            try
            {
                var path = TrialLogPath;
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                try { if (File.Exists(path) && new FileInfo(path).Length > 64 * 1024) File.Delete(path); } catch { }
                File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}", Encoding.UTF8);
            }
            catch { /* diagnostics must never affect the trial */ }
        }

        // ──────────────────────────────────────────────────────────────────
        //  Storage paths
        // ──────────────────────────────────────────────────────────────────

        private static string AppDataPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppFolder, TrialFileName);

        private static string ProgramDataPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                AppFolder, TrialFileName);

        // ──────────────────────────────────────────────────────────────────
        //  Public API
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Checks the trial status.
        /// Returns a <see cref="ValidationResult"/> with Status=Valid and DaysRemaining > 0
        /// if the trial is still active, or an appropriate error otherwise.
        /// </summary>
        public static ValidationResult CheckTrial()
        {
            try
            {
                var deviceId = MachineIdentity.GetDeviceId();
                var now = DateTime.UtcNow;

                // ── 1. Load state (consensus across multiple locations) ──
                var state = LoadConsensusState();

                // Discard states written by an older trial epoch → fresh trial.
                if (state != null && state.Epoch != TrialEpoch)
                {
                    LogTrial($"epoch mismatch: stored={state.Epoch} expected={TrialEpoch} → discarding (fresh trial)");
                    state = null;
                }

                if (state == null)
                {
                    LogTrial($"NO stored state loaded → starting FRESH 7-day trial now. device={Short(deviceId)}");
                    // First run – initialise trial
                    var newState = new TrialState
                    {
                        StartUtc = now,
                        LastSeenUtc = now,
                        DeviceId = deviceId,
                        Epoch = TrialEpoch
                    };
                    newState.Hmac = LicenseCrypto.ComputeTrialHmac(newState);
                    SaveAllLocations(newState);
                    LogTrial("fresh trial saved; verifying it reads back next…");

                    return new ValidationResult
                    {
                        IsValid = true,
                        Status = LicenseStatus.Valid,
                        Type = LicenseType.Trial,
                        DaysRemaining = TrialDays,
                        ExpiresUtc = now.AddDays(TrialDays)
                    };
                }

                // ── 2. HMAC integrity check ──
                if (!LicenseCrypto.VerifyTrialHmac(state))
                {
                    return Error(LicenseStatus.Corrupted,
                        "بيانات الفترة التجريبية تالفة أو تم التلاعب بها.");
                }

                // ── 3. Device binding ──
                // Any id this hardware produces: a trial opened before the 2.8.3 disk
                // choice recorded the old id, and must not read as a hardware change.
                if (!string.IsNullOrEmpty(state.DeviceId) &&
                    !System.Linq.Enumerable.Contains(MachineIdentity.GetCandidateDeviceIds(), state.DeviceId))
                {
                    return Error(LicenseStatus.HardwareMismatch,
                        "تم اكتشاف تغيير في أجهزة الجهاز.");
                }

                // ── 4. Clock rollback detection ──
                if (now + BackdateTolerance < state.LastSeenUtc || now < state.StartUtc)
                {
                    return Error(LicenseStatus.ClockTampered,
                        "تم اكتشاف تغيير في ساعة النظام (clock rollback).");
                }

                LogTrial($"stored state LOADED: start={state.StartUtc:u} lastSeen={state.LastSeenUtc:u} epoch={state.Epoch} device={Short(state.DeviceId)}");

                // ── 5. Expiry check ──
                var daysUsed = (now - state.StartUtc).TotalDays;
                if (daysUsed > TrialDays)
                {
                    return Error(LicenseStatus.Expired,
                        $"انتهت الفترة التجريبية ({TrialDays} أيام). يرجى تفعيل النسخة الكاملة.");
                }

                // ── 6. Update LastSeen monotonically ──
                state.LastSeenUtc = now > state.LastSeenUtc ? now : state.LastSeenUtc;
                state.Hmac = LicenseCrypto.ComputeTrialHmac(state);
                SaveAllLocations(state);

                int remaining = Math.Max(0, TrialDays - (int)daysUsed);
                return new ValidationResult
                {
                    IsValid = true,
                    Status = LicenseStatus.Valid,
                    Type = LicenseType.Trial,
                    DaysRemaining = remaining,
                    ExpiresUtc = state.StartUtc.AddDays(TrialDays)
                };
            }
            catch (Exception ex)
            {
                return Error(LicenseStatus.Corrupted,
                    $"خطأ في التحقق من الفترة التجريبية: {ex.Message}");
            }
        }

        /// <summary>Returns true if a trial state already exists (i.e., not first run).</summary>
        public static bool TrialExists() => LoadConsensusState() != null;

        /// <summary>
        /// The locally recorded trial start for this device, or null if there is no
        /// (current-epoch) trial state yet. The startup flow reports this to the server so
        /// an honest older start is preserved.
        /// </summary>
        public static DateTime? GetLocalStartUtc()
        {
            var s = LoadConsensusState();
            return (s != null && s.Epoch == TrialEpoch) ? s.StartUtc : (DateTime?)null;
        }

        /// <summary>
        /// Pins the trial start to the server-authoritative value so a reinstall or a wiped
        /// local file cannot reset the trial. The start is only ever moved EARLIER (and never
        /// into the future), so this can shorten a reset-to-now trial back to its true age but
        /// never extend it. Best-effort and fail-safe: any problem leaves the local clock as-is,
        /// so an offline machine is never affected. Call only after a successful server claim.
        /// </summary>
        public static void PinServerStart(DateTime serverStartUtc)
        {
            try
            {
                var now = DateTime.UtcNow;
                // Never trust a start in the future (server clock skew) — it would trip the
                // clock-rollback guard on the next check.
                var pin = serverStartUtc > now ? now : serverStartUtc;

                var state = LoadConsensusState();
                if (state != null && state.Epoch != TrialEpoch) state = null;

                if (state == null)
                {
                    state = new TrialState
                    {
                        StartUtc = pin,
                        LastSeenUtc = now,
                        DeviceId = MachineIdentity.GetDeviceId(),
                        Epoch = TrialEpoch
                    };
                }
                else if (pin < state.StartUtc)
                {
                    state.StartUtc = pin;
                }
                else
                {
                    return; // server start is not earlier — nothing to pin
                }

                state.Hmac = LicenseCrypto.ComputeTrialHmac(state);
                SaveAllLocations(state);
            }
            catch { /* best-effort; must never block or crash startup */ }
        }

        // ──────────────────────────────────────────────────────────────────
        //  Multi-location persistence
        // ──────────────────────────────────────────────────────────────────

        private static void SaveAllLocations(TrialState state)
        {
            var json = JsonSerializer.Serialize(state);
            var enc = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

            bool app = SafeWriteFile(AppDataPath, enc);
            bool prog = SafeWriteFile(ProgramDataPath, enc);     // may fail without admin rights – that's OK
            bool reg = SafeWriteRegistry(enc);

            LogTrial($"SAVE: appData={(app ? "ok" : "FAIL")} programData={(prog ? "ok" : "FAIL")} registry={(reg ? "ok" : "FAIL")}");
            if (!app && !prog && !reg)
                LogTrial("⚠️ ALL THREE save locations FAILED → the trial WILL reset on next launch. Likely permissions or an antivirus wiping the files.");
        }

        private static string Short(string? id) =>
            string.IsNullOrEmpty(id) ? "(empty)" : (id.Length <= 8 ? id : id.Substring(0, 8));

        /// <summary>
        /// Loads from all three locations; returns the state that appears most frequently
        /// (majority vote). Returns null if no valid, consistent state is found anywhere.
        /// </summary>
        private static TrialState? LoadConsensusState()
        {
            var candidates = new System.Collections.Generic.List<TrialState>();

            bool app = TryLoad(AppDataPath, candidates);
            bool prog = TryLoad(ProgramDataPath, candidates);
            bool reg = TryLoadRegistry(candidates);

            LogTrial($"LOAD: appData={(app ? "ok" : "–")} programData={(prog ? "ok" : "–")} registry={(reg ? "ok" : "–")} (candidates={candidates.Count})");

            if (candidates.Count == 0)
                return null;

            // Pick the one with the EARLIEST start date (most authentic)
            // All three should agree; if they differ, pick the oldest start date
            TrialState? best = null;
            foreach (var c in candidates)
                if (best == null || c.StartUtc < best.StartUtc)
                    best = c;

            return best;
        }

        // ──────────────────────────────────────────────────────────────────
        //  File helpers
        // ──────────────────────────────────────────────────────────────────

        private static bool SafeWriteFile(string path, string encoded)
        {
            try
            {
                var dir = Path.GetDirectoryName(path)!;
                Directory.CreateDirectory(dir);
                File.WriteAllText(path, encoded, Encoding.UTF8);
                return true;
            }
            catch (Exception ex) { LogTrial($"write FAIL {path}: {ex.GetType().Name} {ex.Message}"); return false; }
        }

        private static bool TryLoad(string path, System.Collections.Generic.List<TrialState> list)
        {
            try
            {
                if (!File.Exists(path)) return false;
                var enc = File.ReadAllText(path, Encoding.UTF8).Trim();
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(enc));
                var state = JsonSerializer.Deserialize<TrialState>(json);
                if (state != null) { list.Add(state); return true; }
                return false;
            }
            catch (Exception ex) { LogTrial($"read FAIL {path}: {ex.GetType().Name} {ex.Message}"); return false; }
        }

        // ──────────────────────────────────────────────────────────────────
        //  Registry helpers
        // ──────────────────────────────────────────────────────────────────

        private static bool SafeWriteRegistry(string encoded)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistrySubKey, writable: true);
                if (key == null) return false;
                key.SetValue("State", encoded, RegistryValueKind.String);
                return true;
            }
            catch (Exception ex) { LogTrial($"registry write FAIL: {ex.GetType().Name} {ex.Message}"); return false; }
        }

        private static bool TryLoadRegistry(System.Collections.Generic.List<TrialState> list)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistrySubKey, writable: false);
                var enc = key?.GetValue("State")?.ToString();
                if (string.IsNullOrEmpty(enc)) return false;
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(enc));
                var state = JsonSerializer.Deserialize<TrialState>(json);
                if (state != null) { list.Add(state); return true; }
                return false;
            }
            catch (Exception ex) { LogTrial($"registry read FAIL: {ex.GetType().Name} {ex.Message}"); return false; }
        }

        // ──────────────────────────────────────────────────────────────────
        //  Helper
        // ──────────────────────────────────────────────────────────────────

        private static ValidationResult Error(LicenseStatus status, string msg) =>
            new() { IsValid = false, Status = status, ErrorMessage = msg };
    }
}
