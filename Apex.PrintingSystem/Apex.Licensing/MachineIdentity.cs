using System;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace Apex.Licensing
{
    /// <summary>
    /// Builds a stable, hardware-bound device fingerprint using four independent sources.
    /// The final DeviceId is the first 32 hex chars of SHA-256( join('|', sources) ).
    /// </summary>
    public static class MachineIdentity
    {
        // ──────────────────────────────────────────────────────────────────
        //  Public API
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns a stable 32-character hex Device ID for this machine.
        /// Returns "UNKNOWN" if all hardware reads fail.
        /// </summary>
        public static string GetDeviceId()
        {
            var parts = new[]
            {
                GetMotherboardUUID(),
                GetCpuId(),
                GetDriveSerial(),
                GetOsMachineGuid()
            };

            var raw = string.Join("|", parts);
            if (string.IsNullOrWhiteSpace(raw.Replace("|", "")))
                return "UNKNOWN";

            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
            return Convert.ToHexString(hash)[..32]; // 16 bytes → 32 hex chars
        }

        /// <summary>
        /// Returns a <see cref="DeviceIdentityInfo"/> with both the raw and display-formatted DeviceId.
        /// Display format: XXXXXXXX-XXXXXXXX-XXXXXXXX-XXXXXXXX  (4 groups of 8 hex chars)
        /// </summary>
        public static DeviceIdentityInfo GetDeviceInfo()
        {
            var raw = GetDeviceId();
            var display = raw.Length == 32
                ? $"{raw[..8]}-{raw[8..16]}-{raw[16..24]}-{raw[24..32]}"
                : raw;

            return new DeviceIdentityInfo { DeviceId = raw, DisplayId = display };
        }

        // ──────────────────────────────────────────────────────────────────
        //  Private hardware-reading helpers
        // ──────────────────────────────────────────────────────────────────

        private static string GetMotherboardUUID()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT UUID FROM Win32_ComputerSystemProduct");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var v = obj["UUID"]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(v) && v != "00000000-0000-0000-0000-000000000000")
                        return v;
                }
            }
            catch { /* VM / restricted environment */ }
            return "";
        }

        private static string GetCpuId()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var v = obj["ProcessorId"]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(v))
                        return v;
                }
            }
            catch { }
            return "";
        }

        private static string GetDriveSerial()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_PhysicalMedia");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var v = obj["SerialNumber"]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(v))
                        return v;
                }
            }
            catch { }
            return "";
        }

        private static string GetOsMachineGuid()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Cryptography", writable: false);
                return key?.GetValue("MachineGuid")?.ToString() ?? "";
            }
            catch { }
            return "";
        }
    }
}
