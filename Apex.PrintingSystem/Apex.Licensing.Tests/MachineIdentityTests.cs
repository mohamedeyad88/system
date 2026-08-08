using System;
using System.Text.RegularExpressions;
using Apex.Licensing;
using Xunit;

namespace Apex.Licensing.Tests
{
    /// <summary>
    /// Tests for <see cref="MachineIdentity"/>.
    /// These run against the real hardware of the test machine.
    /// </summary>
    public class MachineIdentityTests
    {
        // ──────────────────────────────────────────────────────────────────
        //  DeviceId format
        // ──────────────────────────────────────────────────────────────────

        [Fact]
        public void GetDeviceId_Returns32HexChars()
        {
            var id = MachineIdentity.GetDeviceId();

            // Either 32 hex chars or "UNKNOWN" (degenerate environment)
            Assert.True(
                id == "UNKNOWN" || (id.Length == 32 && Regex.IsMatch(id, @"^[0-9A-F]{32}$")),
                $"Unexpected DeviceId: '{id}'");
        }

        [Fact]
        public void GetDeviceId_IsUpperCase()
        {
            var id = MachineIdentity.GetDeviceId();
            if (id == "UNKNOWN") return; // skip in degenerate env

            Assert.Equal(id.ToUpperInvariant(), id);
        }

        [Fact]
        public void GetDeviceId_IsStableBetweenCalls()
        {
            var id1 = MachineIdentity.GetDeviceId();
            var id2 = MachineIdentity.GetDeviceId();

            Assert.Equal(id1, id2);
        }

        // ──────────────────────────────────────────────────────────────────
        //  DeviceIdentityInfo
        // ──────────────────────────────────────────────────────────────────

        [Fact]
        public void GetDeviceInfo_DeviceIdMatchesGetDeviceId()
        {
            var info = MachineIdentity.GetDeviceInfo();
            var raw = MachineIdentity.GetDeviceId();

            Assert.Equal(raw, info.DeviceId);
        }

        [Fact]
        public void GetDeviceInfo_DisplayIdHasGroupFormat_WhenValidId()
        {
            var info = MachineIdentity.GetDeviceInfo();

            if (info.DeviceId == "UNKNOWN")
            {
                // Display falls back to raw "UNKNOWN"
                Assert.Equal("UNKNOWN", info.DisplayId);
                return;
            }

            // Expected pattern: XXXXXXXX-XXXXXXXX-XXXXXXXX-XXXXXXXX
            Assert.Matches(@"^[0-9A-F]{8}-[0-9A-F]{8}-[0-9A-F]{8}-[0-9A-F]{8}$",
                info.DisplayId);
        }

        [Fact]
        public void GetDeviceInfo_DisplayIdPartsConcatenateToDeviceId()
        {
            var info = MachineIdentity.GetDeviceInfo();
            if (info.DeviceId == "UNKNOWN") return;

            var withoutDashes = info.DisplayId.Replace("-", "");
            Assert.Equal(info.DeviceId, withoutDashes);
        }

        [Fact]
        public void GetDeviceInfo_IsStableBetweenCalls()
        {
            var a = MachineIdentity.GetDeviceInfo();
            var b = MachineIdentity.GetDeviceInfo();

            Assert.Equal(a.DeviceId, b.DeviceId);
            Assert.Equal(a.DisplayId, b.DisplayId);
        }
    }
}
