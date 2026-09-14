using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Security.Cryptography;
using System.Text;

namespace Apex.Licensing
{
    /// <summary>A physical disk as Windows reports it, in Windows' own enumeration order.</summary>
    public sealed record DiskSerial(string Tag, string Serial, bool IsRemovable);

    /// <summary>The hardware facts a device id is built from.</summary>
    public sealed record HardwareFacts(string BoardUuid, string CpuId, IReadOnlyList<DiskSerial> Disks, string? BootDiskTag);

    /// <summary>
    /// Builds a stable, hardware-bound device fingerprint from three firmware/hardware
    /// sources (motherboard UUID, CPU id, physical-disk serial). All three survive a
    /// Windows reinstall / format on the SAME machine, so the DeviceId is unchanged
    /// after a format and the license re-activates without support intervention.
    ///
    /// The Windows MachineGuid is deliberately NOT used: it is regenerated on every
    /// Windows reinstall, which would change the fingerprint and lock out a customer
    /// who merely formatted the same PC.
    ///
    /// <para><b>Which disk.</b> The id used to take the first serial in
    /// Win32_PhysicalMedia — whatever order WMI enumerates in, which is not guaranteed and
    /// is not the boot disk. On the owner's own PC it listed PHYSICALDRIVE2, an old data
    /// HDD, ahead of the SSD Windows boots from. That disk failing, being unplugged, or a
    /// USB stick enumerating first would change the id and lock a paying shop out with
    /// "license for another device". The id now uses the disk holding the Windows system
    /// volume, and never a USB/removable disk.</para>
    ///
    /// <para><b>Compatibility.</b> Changing the formula must not lock out anyone licensed
    /// under the old one. A license is therefore accepted when it names ANY id this
    /// hardware can produce (<see cref="GetCandidateDeviceIds"/>): the current id, the old
    /// first-enumerated-disk id, and the id for each internal disk. All of them still
    /// require the same motherboard and CPU.</para>
    ///
    /// The final DeviceId is the first 32 hex chars of SHA-256( join('|', sources) ).
    /// </summary>
    public static class MachineIdentity
    {
        private static readonly Lazy<HardwareFacts> Facts = new(ReadHardware);

        // ──────────────────────────────────────────────────────────────────
        //  Public API
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns a stable 32-character hex Device ID for this machine.
        /// Returns "UNKNOWN" if all hardware reads fail.
        /// </summary>
        public static string GetDeviceId() => CanonicalId(Facts.Value);

        /// <summary>Every id this machine accepts a license for; the current id first.</summary>
        public static IReadOnlyList<string> GetCandidateDeviceIds() => CandidateIds(Facts.Value);

        /// <summary>
        /// Returns a <see cref="DeviceIdentityInfo"/> with both the raw and display-formatted DeviceId.
        /// Display format: XXXXXXXX-XXXXXXXX-XXXXXXXX-XXXXXXXX  (4 groups of 8 hex chars)
        /// </summary>
        public static DeviceIdentityInfo GetDeviceInfo() => ToInfo(GetDeviceId());

        public static DeviceIdentityInfo ToInfo(string raw)
        {
            var display = raw.Length == 32
                ? $"{raw[..8]}-{raw[8..16]}-{raw[16..24]}-{raw[24..32]}"
                : raw;
            return new DeviceIdentityInfo { DeviceId = raw, DisplayId = display };
        }

        // ──────────────────────────────────────────────────────────────────
        //  The formula (pure — tested without real hardware)
        // ──────────────────────────────────────────────────────────────────

        public static string ComputeId(string boardUuid, string cpuId, string diskSerial)
        {
            var raw = string.Join("|", boardUuid, cpuId, diskSerial);
            if (string.IsNullOrWhiteSpace(raw.Replace("|", "")))
                return "UNKNOWN";

            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
            return Convert.ToHexString(hash)[..32]; // 16 bytes → 32 hex chars
        }

        /// <summary>The boot disk; else the lowest-numbered internal disk; else the old choice.</summary>
        public static string CanonicalId(HardwareFacts f)
        {
            var internalDisks = f.Disks.Where(d => !d.IsRemovable && d.Serial.Length > 0).ToList();

            var disk = internalDisks.FirstOrDefault(d =>
                           f.BootDiskTag != null && string.Equals(d.Tag, f.BootDiskTag, StringComparison.OrdinalIgnoreCase))
                       ?? internalDisks.OrderBy(d => DriveNumber(d.Tag)).FirstOrDefault();

            return ComputeId(f.BoardUuid, f.CpuId, disk?.Serial ?? LegacyDiskSerial(f));
        }

        /// <summary>The id every install before 2.8.3 produced: the first serial WMI listed.</summary>
        public static string LegacyId(HardwareFacts f) => ComputeId(f.BoardUuid, f.CpuId, LegacyDiskSerial(f));

        public static IReadOnlyList<string> CandidateIds(HardwareFacts f)
        {
            var ids = new List<string> { CanonicalId(f), LegacyId(f) };
            ids.AddRange(f.Disks.Where(d => !d.IsRemovable && d.Serial.Length > 0)
                                .Select(d => ComputeId(f.BoardUuid, f.CpuId, d.Serial)));
            return ids.Distinct(StringComparer.Ordinal).ToList();
        }

        private static string LegacyDiskSerial(HardwareFacts f) =>
            f.Disks.FirstOrDefault(d => d.Serial.Length > 0)?.Serial ?? "";

        private static int DriveNumber(string tag)
        {
            var digits = new string(tag.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
            return int.TryParse(digits, out var n) ? n : int.MaxValue;
        }

        // ──────────────────────────────────────────────────────────────────
        //  Private hardware-reading helpers
        // ──────────────────────────────────────────────────────────────────

        private static HardwareFacts ReadHardware() =>
            new(GetMotherboardUUID(), GetCpuId(), GetDisks(), GetBootDiskTag());

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

        /// <summary>Win32_PhysicalMedia in enumeration order (the legacy id depends on it).</summary>
        private static IReadOnlyList<DiskSerial> GetDisks()
        {
            var removable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var drives = new ManagementObjectSearcher("SELECT DeviceID, InterfaceType, MediaType FROM Win32_DiskDrive");
                foreach (ManagementObject obj in drives.Get())
                {
                    var iface = obj["InterfaceType"]?.ToString() ?? "";
                    var media = obj["MediaType"]?.ToString() ?? "";
                    if (iface.Equals("USB", StringComparison.OrdinalIgnoreCase) ||
                        media.Contains("Removable", StringComparison.OrdinalIgnoreCase) ||
                        media.Contains("External", StringComparison.OrdinalIgnoreCase))
                        removable.Add(obj["DeviceID"]?.ToString() ?? "");
                }
            }
            catch { }

            var disks = new List<DiskSerial>();
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Tag, SerialNumber FROM Win32_PhysicalMedia");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var tag = obj["Tag"]?.ToString() ?? "";
                    var serial = obj["SerialNumber"]?.ToString()?.Trim() ?? "";
                    disks.Add(new DiskSerial(tag, serial, removable.Contains(tag)));
                }
            }
            catch { }
            return disks;
        }

        /// <summary>The physical disk holding the Windows system volume, e.g. \\.\PHYSICALDRIVE0.</summary>
        private static string? GetBootDiskTag()
        {
            try
            {
                var systemDrive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
                using var partitions = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{systemDrive}'}} WHERE AssocClass=Win32_LogicalDiskToPartition");
                foreach (ManagementObject partition in partitions.Get())
                {
                    using var drives = new ManagementObjectSearcher(
                        $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partition["DeviceID"]}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition");
                    foreach (ManagementObject drive in drives.Get())
                    {
                        var id = drive["DeviceID"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(id)) return id;
                    }
                }
            }
            catch { }
            return null;
        }
    }
}
