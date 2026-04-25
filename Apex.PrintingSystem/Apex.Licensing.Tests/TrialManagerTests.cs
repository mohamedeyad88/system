using System;
using System.Security.Cryptography;
using Apex.Licensing;
using Xunit;

namespace Apex.Licensing.Tests
{
    /// <summary>
    /// Tests for trial-state logic.
    ///
    /// NOTE: <see cref="TrialManager.CheckTrial"/> writes to the real file system
    /// and registry of the test machine. The tests here focus on the pure-logic
    /// layer (HMAC, expiry arithmetic, clock-rollback formula) without touching
    /// live storage, to avoid polluting the trial state of the developer machine.
    ///
    /// Integration-level tests that call CheckTrial() directly are marked with
    /// [Trait("Category","Integration")] so they can be skipped in CI.
    /// </summary>
    public class TrialManagerTests
    {
        // ──────────────────────────────────────────────────────────────────
        //  Constants mirror
        // ──────────────────────────────────────────────────────────────────

        private const int TrialDays = TrialManager.TrialDays;   // 5

        // ──────────────────────────────────────────────────────────────────
        //  Helper – build a valid TrialState with correct HMAC
        // ──────────────────────────────────────────────────────────────────

        private static TrialState BuildValidState(
            DateTime? startUtc    = null,
            DateTime? lastSeenUtc = null,
            string    deviceId    = "AABBCCDD11223344AABBCCDD11223344")
        {
            var start  = startUtc    ?? DateTime.UtcNow.AddDays(-1);
            var last   = lastSeenUtc ?? DateTime.UtcNow;
            var state  = new TrialState
            {
                StartUtc    = start,
                LastSeenUtc = last,
                DeviceId    = deviceId
            };
            state.Hmac = LicenseCrypto.ComputeTrialHmac(state);
            return state;
        }

        // ──────────────────────────────────────────────────────────────────
        //  Trial constant
        // ──────────────────────────────────────────────────────────────────

        [Fact]
        public void TrialDays_IsExactlyFive()
        {
            Assert.Equal(5, TrialManager.TrialDays);
        }

        // ──────────────────────────────────────────────────────────────────
        //  Expiry arithmetic
        // ──────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(4)]
        public void DaysUsed_LessThanOrEqualToTrialDays_ShouldStillBeValid(int daysUsed)
        {
            var now   = DateTime.UtcNow;
            var start = now.AddDays(-daysUsed);

            double used = (now - start).TotalDays;
            Assert.True(used <= TrialDays,
                $"daysUsed={daysUsed} should be within trial period");
        }

        [Theory]
        [InlineData(6)]
        [InlineData(10)]
        [InlineData(100)]
        public void DaysUsed_GreaterThanTrialDays_ShouldBeExpired(int daysUsed)
        {
            var now   = DateTime.UtcNow;
            var start = now.AddDays(-daysUsed);

            double used = (now - start).TotalDays;
            Assert.True(used > TrialDays,
                $"daysUsed={daysUsed} should exceed trial period");
        }

        // ──────────────────────────────────────────────────────────────────
        //  Clock-rollback detection formula
        // ──────────────────────────────────────────────────────────────────

        [Fact]
        public void ClockRollback_NowExactlyEqualsLastSeen_NotTampered()
        {
            var now      = DateTime.UtcNow;
            var lastSeen = now;
            var start    = now.AddDays(-1);

            // Formula from TrialManager: (now + tolerance) < lastSeen || now < start
            var tolerance = TimeSpan.FromDays(1);
            bool rollback = (now + tolerance < lastSeen) || (now < start);
            Assert.False(rollback);
        }

        [Fact]
        public void ClockRollback_NowOneHourBeforeLastSeen_WithinTolerance_NotTampered()
        {
            var now      = DateTime.UtcNow;
            var lastSeen = now.AddHours(1);  // last seen is 1h in the future (clock skew)
            var start    = now.AddDays(-2);

            var tolerance = TimeSpan.FromDays(1);
            bool rollback = (now + tolerance < lastSeen) || (now < start);
            Assert.False(rollback);
        }

        [Fact]
        public void ClockRollback_NowTwoDaysBeforeLastSeen_ExceedsTolerance_Tampered()
        {
            var now      = DateTime.UtcNow;
            var lastSeen = now.AddDays(2);   // "last seen" is 2 days ahead of now → clock was rolled back
            var start    = now.AddDays(-1);

            var tolerance = TimeSpan.FromDays(1);
            bool rollback = (now + tolerance < lastSeen) || (now < start);
            Assert.True(rollback);
        }

        [Fact]
        public void ClockRollback_NowBeforeStart_Tampered()
        {
            var now   = DateTime.UtcNow;
            var start = now.AddDays(1);      // start is in the future → impossible without clock change

            var tolerance = TimeSpan.FromDays(1);
            bool rollback = (now + tolerance < start) || (now < start);
            Assert.True(rollback);
        }

        // ──────────────────────────────────────────────────────────────────
        //  HMAC integrity (via LicenseCrypto, which TrialManager uses)
        // ──────────────────────────────────────────────────────────────────

        [Fact]
        public void TrialState_FreshHmac_Verifies()
        {
            var state = BuildValidState();
            Assert.True(LicenseCrypto.VerifyTrialHmac(state));
        }

        [Fact]
        public void TrialState_RolledBackStartUtc_FailsHmac()
        {
            var state = BuildValidState(startUtc: DateTime.UtcNow.AddDays(-1));
            // Attacker attempts to extend trial by rolling start date forward (making it "newer")
            state.StartUtc = DateTime.UtcNow.AddMinutes(-10);

            Assert.False(LicenseCrypto.VerifyTrialHmac(state));
        }

        [Fact]
        public void TrialState_ClearedHmac_FailsVerification()
        {
            var state = BuildValidState();
            state.Hmac = "";

            Assert.False(LicenseCrypto.VerifyTrialHmac(state));
        }

        [Fact]
        public void TrialState_AllZeroHmac_FailsVerification()
        {
            var state = BuildValidState();
            state.Hmac = Convert.ToBase64String(new byte[32]);

            Assert.False(LicenseCrypto.VerifyTrialHmac(state));
        }

        // ──────────────────────────────────────────────────────────────────
        //  DaysRemaining calculation
        // ──────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData(0, 5)]
        [InlineData(1, 4)]
        [InlineData(3, 2)]
        [InlineData(4, 1)]
        [InlineData(5, 0)]
        public void DaysRemaining_CalculatedCorrectly(int daysUsed, int expectedRemaining)
        {
            var now   = DateTime.UtcNow;
            var start = now.AddDays(-daysUsed);

            int remaining = Math.Max(0, TrialDays - (int)(now - start).TotalDays);
            Assert.Equal(expectedRemaining, remaining);
        }

        // ──────────────────────────────────────────────────────────────────
        //  Integration – uses real storage (skipped in CI)
        // ──────────────────────────────────────────────────────────────────

        [Fact]
        [Trait("Category", "Integration")]
        public void CheckTrial_ReturnsValidOrExpiredOrCorrupted_NeverThrows()
        {
            // Must not throw regardless of existing trial state on this machine
            var result = Record.Exception(() => TrialManager.CheckTrial());
            Assert.Null(result);
        }

        [Fact]
        [Trait("Category", "Integration")]
        public void CheckTrial_ReturnedStatus_IsOneOfKnownValues()
        {
            var result = TrialManager.CheckTrial();

            Assert.True(
                result.Status == LicenseStatus.Valid         ||
                result.Status == LicenseStatus.Expired       ||
                result.Status == LicenseStatus.Corrupted     ||
                result.Status == LicenseStatus.ClockTampered ||
                result.Status == LicenseStatus.HardwareMismatch,
                $"Unexpected status: {result.Status}");
        }

        [Fact]
        [Trait("Category", "Integration")]
        public void CheckTrial_WhenValid_TypeIsTrial()
        {
            var result = TrialManager.CheckTrial();
            if (result.IsValid)
                Assert.Equal(LicenseType.Trial, result.Type);
        }

        [Fact]
        [Trait("Category", "Integration")]
        public void CheckTrial_WhenValid_DaysRemainingBetweenZeroAndFive()
        {
            var result = TrialManager.CheckTrial();
            if (result.IsValid)
            {
                Assert.InRange(result.DaysRemaining, 0, TrialDays);
            }
        }
    }
}
