using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Apex.Licensing;
using Microsoft.Win32;
using Xunit;

namespace Apex.Licensing.Tests
{
    /// <summary>
    /// End-to-end proof that CheckTrial() ENFORCES the 7-day limit against a persisted
    /// state — the path a field report said an old build got wrong (the trial "stayed
    /// fixed" and the app kept running past 7 days). The pure-arithmetic tests in
    /// TrialManagerTests prove the formula; these prove the real load→check path.
    ///
    /// They write to the machine's actual trial locations, so they back up whatever is
    /// there and restore it in Dispose, and run in a non-parallel collection so no
    /// other test races the same files.
    /// </summary>
    [Collection("TrialStorage")]
    public sealed class TrialEnforcementIntegrationTests : IDisposable
    {
        private const string AppFolder = "ApexPrintingSystem";
        private const string TrialFileName = "apex_trial.dat";
        private const string RegistrySubKey = @"SOFTWARE\ApexPrintingSystem\Trial";

        private static string AppDataPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolder, TrialFileName);
        private static string ProgramDataPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), AppFolder, TrialFileName);

        // ── backups of the real state, restored in Dispose ──
        private readonly string? _appDataBackup;
        private readonly string? _programDataBackup;
        private readonly string? _registryBackup;

        public TrialEnforcementIntegrationTests()
        {
            _appDataBackup     = ReadFileOrNull(AppDataPath);
            _programDataBackup = ReadFileOrNull(ProgramDataPath);
            _registryBackup    = ReadRegistryOrNull();
        }

        public void Dispose()
        {
            RestoreFile(AppDataPath, _appDataBackup);
            RestoreFile(ProgramDataPath, _programDataBackup);
            RestoreRegistry(_registryBackup);
        }

        // ──────────────────────────────────────────────────────────────────

        [Fact]
        [Trait("Category", "Integration")]
        public void CheckTrial_StateOlderThanSevenDays_IsExpiredAndBlocks()
        {
            Plant(startDaysAgo: 10, tamperHmac: false);

            var result = TrialManager.CheckTrial();

            Assert.False(result.IsValid, "a 10-day-old trial must NOT be valid");
            Assert.Equal(LicenseStatus.Expired, result.Status);
        }

        [Fact]
        [Trait("Category", "Integration")]
        public void CheckTrial_StateWithinSevenDays_IsValidWithRemainingDays()
        {
            Plant(startDaysAgo: 2, tamperHmac: false);

            var result = TrialManager.CheckTrial();

            Assert.True(result.IsValid, "a 2-day-old trial must still be valid");
            Assert.Equal(LicenseStatus.Valid, result.Status);
            Assert.InRange(result.DaysRemaining, 4, 5); // 7 - 2, floored
        }

        [Fact]
        [Trait("Category", "Integration")]
        public void CheckTrial_TamperedState_FailsClosed_NotOpen()
        {
            // A broken/edited state must block (Corrupted), never fall through to "valid".
            Plant(startDaysAgo: 1, tamperHmac: true);

            var result = TrialManager.CheckTrial();

            Assert.False(result.IsValid);
            Assert.Equal(LicenseStatus.Corrupted, result.Status);
        }

        // ──────────────────────────────────────────────────────────────────

        /// <summary>Writes a trial state (with a valid or deliberately broken HMAC) to all
        /// three locations, so CheckTrial's earliest-start consensus sees only this one.</summary>
        private static void Plant(int startDaysAgo, bool tamperHmac)
        {
            var now = DateTime.UtcNow;
            var state = new TrialState
            {
                StartUtc = now.AddDays(-startDaysAgo),
                LastSeenUtc = now.AddMinutes(-5),
                DeviceId = MachineIdentity.GetDeviceId(),
                Epoch = TrialManager.TrialEpoch,
            };
            state.Hmac = LicenseCrypto.ComputeTrialHmac(state);
            if (tamperHmac)
                state.Hmac = state.Hmac.Length > 0
                    ? new string('0', state.Hmac.Length)
                    : "deadbeef";

            var enc = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(state)));

            WriteFile(AppDataPath, enc);
            WriteFile(ProgramDataPath, enc);   // may no-op without admin — AppData+registry suffice
            WriteRegistry(enc);
        }

        // ── file/registry helpers (mirror TrialManager's storage) ──
        private static string? ReadFileOrNull(string p)
        {
            try { return File.Exists(p) ? File.ReadAllText(p, Encoding.UTF8) : null; } catch { return null; }
        }
        private static void WriteFile(string p, string enc)
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllText(p, enc, Encoding.UTF8); } catch { }
        }
        private static void RestoreFile(string p, string? backup)
        {
            try
            {
                if (backup is null) { if (File.Exists(p)) File.Delete(p); }
                else File.WriteAllText(p, backup, Encoding.UTF8);
            }
            catch { }
        }
        private static string? ReadRegistryOrNull()
        {
            try { using var k = Registry.CurrentUser.OpenSubKey(RegistrySubKey); return k?.GetValue("State")?.ToString(); }
            catch { return null; }
        }
        private static void WriteRegistry(string enc)
        {
            try { using var k = Registry.CurrentUser.CreateSubKey(RegistrySubKey, true); k?.SetValue("State", enc, RegistryValueKind.String); }
            catch { }
        }
        private static void RestoreRegistry(string? backup)
        {
            try
            {
                using var k = Registry.CurrentUser.CreateSubKey(RegistrySubKey, true);
                if (backup is null) k?.DeleteValue("State", throwOnMissingValue: false);
                else k?.SetValue("State", backup, RegistryValueKind.String);
            }
            catch { }
        }
    }

    /// <summary>Serialises the trial-storage integration tests so none races the files.</summary>
    [CollectionDefinition("TrialStorage", DisableParallelization = true)]
    public sealed class TrialStorageCollection { }
}
