using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;

namespace Apex.Services.Printing.Color
{
    public record IccProfile(
        string FilePath,
        string ProfileName,
        string DeviceClass,    // "prtr", "mntr", "scnr", "Unknown"
        string ColorSpace,     // "RGB", "CMYK", "GRAY", "Unknown"
        string Pcs,            // "XYZ", "Lab", "Unknown"
        int Version,
        long FileSizeBytes
    );

    public sealed class IccProfileManager
    {
        private static readonly Lazy<IccProfileManager> _instance =
            new(() => new IccProfileManager(), System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

        public static IccProfileManager Instance => _instance.Value;

        private readonly ConcurrentDictionary<string, IccProfile> _profiles =
            new(StringComparer.OrdinalIgnoreCase);

        private static readonly string[] ScanFolders =
        {
            @"C:\Windows\System32\spool\drivers\color\",
            @"C:\Windows\System32\Color\"
        };

        private IccProfileManager()
        {
            ScanAllFolders();
        }

        // ── Scanning ──────────────────────────────────────────────────────────

        private void ScanAllFolders()
        {
            foreach (var folder in ScanFolders)
            {
                ScanFolder(folder);
            }
            Debug.WriteLine($"[IccProfileManager] Loaded {_profiles.Count} ICC profile(s).");
        }

        private void ScanFolder(string folderPath)
        {
            try
            {
                if (!Directory.Exists(folderPath))
                {
                    Debug.WriteLine($"[IccProfileManager] Folder not found, skipping: {folderPath}");
                    return;
                }

                var files = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f =>
                    {
                        var ext = Path.GetExtension(f);
                        return string.Equals(ext, ".icc", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(ext, ".icm", StringComparison.OrdinalIgnoreCase);
                    });

                foreach (var file in files)
                {
                    try
                    {
                        var profile = ParseIccFile(file);
                        if (profile is not null)
                        {
                            _profiles[file] = profile;
                            Debug.WriteLine($"[IccProfileManager] Loaded: {profile.ProfileName} ({profile.DeviceClass}/{profile.ColorSpace})");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[IccProfileManager] Failed to parse {file}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[IccProfileManager] Error scanning folder {folderPath}: {ex.Message}");
            }
        }

        private static IccProfile? ParseIccFile(string filePath)
        {
            const int HeaderSize = 128;

            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists || fileInfo.Length < HeaderSize)
                return null;

            byte[] header = new byte[HeaderSize];
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            int bytesRead = fs.Read(header, 0, HeaderSize);
            if (bytesRead < HeaderSize)
                return null;

            // Offset 0–3: profile size (big-endian uint32)
            uint profileSize = ReadUInt32BigEndian(header, 0);

            // Offset 4–7: preferred CMM (4-char ASCII) — informational only

            // Offset 8–11: version
            int versionMajor = header[8];
            int versionMinor = (header[9] >> 4) & 0x0F;
            int version = versionMajor * 10 + versionMinor;

            // Offset 12–15: device class
            string deviceClassRaw = ReadAscii4(header, 12);
            string deviceClass = deviceClassRaw.TrimEnd('\0', ' ') switch
            {
                "prtr" => "prtr",
                "mntr" => "mntr",
                "scnr" => "scnr",
                _ => "Unknown"
            };

            // Offset 16–19: color space
            string colorSpaceRaw = ReadAscii4(header, 16);
            string colorSpace = colorSpaceRaw.TrimEnd('\0', ' ') switch
            {
                "RGB " or "RGB" => "RGB",
                "CMYK" => "CMYK",
                "GRAY" => "GRAY",
                _ => "Unknown"
            };

            // Offset 20–23: PCS
            string pcsRaw = ReadAscii4(header, 20);
            string pcs = pcsRaw.TrimEnd('\0', ' ') switch
            {
                "XYZ " or "XYZ" => "XYZ",
                "Lab " or "Lab" => "Lab",
                _ => "Unknown"
            };

            // Offset 48–79: profile description (try ASCII)
            string profileName = TryReadDescription(header, 48, 32);
            if (string.IsNullOrWhiteSpace(profileName))
                profileName = Path.GetFileNameWithoutExtension(filePath);

            return new IccProfile(
                FilePath: filePath,
                ProfileName: profileName,
                DeviceClass: deviceClass,
                ColorSpace: colorSpace,
                Pcs: pcs,
                Version: version,
                FileSizeBytes: fileInfo.Length
            );
        }

        // ── Lookup ────────────────────────────────────────────────────────────

        /// <summary>
        /// Attempts to find the ICC profile for the named printer via WMI.
        /// Falls back to the first printer-class profile, or null.
        /// </summary>
        public IccProfile? GetProfileForPrinter(string printerName)
        {
            if (string.IsNullOrWhiteSpace(printerName))
                return GetAllPrinterProfiles().FirstOrDefault();

            try
            {
                string driverName = QueryPrinterDriverName(printerName);
                if (!string.IsNullOrEmpty(driverName))
                {
                    // Search loaded profiles for a partial name match against the driver
                    var match = _profiles.Values.FirstOrDefault(p =>
                        p.DeviceClass == "prtr" &&
                        (p.ProfileName.Contains(driverName, StringComparison.OrdinalIgnoreCase)
                         || driverName.Contains(p.ProfileName, StringComparison.OrdinalIgnoreCase)
                         || Path.GetFileNameWithoutExtension(p.FilePath)
                                .Contains(driverName.Split(' ')[0], StringComparison.OrdinalIgnoreCase)));
                    if (match is not null)
                    {
                        Debug.WriteLine($"[IccProfileManager] Matched profile '{match.ProfileName}' for printer '{printerName}'.");
                        return match;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[IccProfileManager] WMI lookup failed for '{printerName}': {ex.Message}");
            }

            // Fallback: first printer profile
            var fallback = GetAllPrinterProfiles().FirstOrDefault();
            Debug.WriteLine(fallback is not null
                ? $"[IccProfileManager] Using fallback profile '{fallback.ProfileName}' for '{printerName}'."
                : $"[IccProfileManager] No printer profile found for '{printerName}'.");
            return fallback;
        }

        /// <summary>Returns all profiles where DeviceClass == "prtr".</summary>
        public IReadOnlyList<IccProfile> GetAllPrinterProfiles() =>
            _profiles.Values.Where(p => p.DeviceClass == "prtr").ToList();

        /// <summary>Returns every loaded profile.</summary>
        public IReadOnlyList<IccProfile> GetAllProfiles() =>
            _profiles.Values.ToList();

        /// <summary>Re-scans all ICC folders and refreshes the in-memory cache.</summary>
        public void Refresh()
        {
            _profiles.Clear();
            ScanAllFolders();
            Debug.WriteLine($"[IccProfileManager] Refresh complete — {_profiles.Count} profile(s) loaded.");
        }

        // ── WMI Helper ────────────────────────────────────────────────────────

        private static string QueryPrinterDriverName(string printerName)
        {
            try
            {
                string escapedName = printerName.Replace("'", "\\'");
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT DriverName FROM Win32_Printer WHERE Name='{escapedName}'");
                using var results = searcher.Get();
                foreach (ManagementObject obj in results)
                {
                    var driverName = obj["DriverName"]?.ToString();
                    if (!string.IsNullOrEmpty(driverName))
                        return driverName;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[IccProfileManager] WMI query error: {ex.Message}");
            }
            return string.Empty;
        }

        // ── Binary Helpers ────────────────────────────────────────────────────

        private static uint ReadUInt32BigEndian(byte[] buf, int offset) =>
            ((uint)buf[offset] << 24)
            | ((uint)buf[offset + 1] << 16)
            | ((uint)buf[offset + 2] << 8)
            | buf[offset + 3];

        private static string ReadAscii4(byte[] buf, int offset)
        {
            char[] chars = new char[4];
            for (int i = 0; i < 4; i++)
                chars[i] = (char)buf[offset + i];
            return new string(chars);
        }

        private static string TryReadDescription(byte[] buf, int offset, int maxLength)
        {
            int end = Math.Min(offset + maxLength, buf.Length);
            var chars = new System.Text.StringBuilder();
            for (int i = offset; i < end; i++)
            {
                byte b = buf[i];
                if (b == 0) break;
                if (b >= 0x20 && b < 0x7F)
                    chars.Append((char)b);
                else
                    chars.Append('?');
            }
            return chars.ToString().Trim('?', ' ');
        }
    }
}
