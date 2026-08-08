using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Apex.Services.Printing.Color
{
    /// <summary>
    /// Information about a connected display monitor.
    /// </summary>
    public record MonitorInfo(
        string DeviceName,      // e.g. "\\.\DISPLAY1"
        string FriendlyName,    // e.g. "Dell U2722D"
        string CurrentIccPath,  // Full path to active ICC profile, or empty
        string CurrentIccName,  // File name only
        bool   IsPrimary
    );

    /// <summary>
    /// Detects monitor ICC profiles and installs new ones via Windows CMS (mscms.dll / icm.dll).
    /// All operations require Windows; admin rights needed for InstallAndApply.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static class DisplayIccDetector
    {
        // ── Win32 P/Invoke ────────────────────────────────────────────────────

        [DllImport("Gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetICMProfile(IntPtr hDC, ref uint dwSize, [Out] StringBuilder lpFilename);

        [DllImport("Gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateDC(
            string? lpszDriver, string lpszDevice,
            string? lpszOutput, IntPtr lpInitData);

        [DllImport("Gdi32.dll", SetLastError = true)]
        private static extern bool DeleteDC(IntPtr hdc);

        // mscms.dll – Windows Color Management System
        [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool InstallColorProfile(
            string? pMachineName, string pProfileName);

        [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool AssociateColorProfileWithDevice(
            string? pMachineName, string pProfileName, string pDeviceName);

        [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool DisassociateColorProfileFromDevice(
            string? pMachineName, string pProfileName, string pDeviceName);

        // ── WCS (Windows Color System) per-user APIs — no admin rights needed ──
        // Associating/enabling a profile for the CURRENT USER writes to HKCU and the
        // per-user color store, so it never touches the protected System32 folder.
        private enum WCS_PROFILE_MANAGEMENT_SCOPE
        {
            SystemWide  = 0,   // requires admin
            CurrentUser = 1    // per-user, no admin
        }

        [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool WcsAssociateColorProfileWithDevice(
            WCS_PROFILE_MANAGEMENT_SCOPE scope, string pProfileName, string pDeviceName);

        [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool WcsSetUsePerUserProfiles(
            string pDeviceName, uint dwDeviceClass, bool usePerUserProfiles);

        // Device class FOURCC for a monitor ('mntr' big-endian).
        private const uint CLASS_MONITOR = 0x6D6E7472;

        // EnumDisplayDevices to enumerate active monitors
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplayDevices(
            string? lpDevice, uint iDevNum,
            ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]  public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public uint StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        private const uint DISPLAY_DEVICE_ACTIVE  = 0x00000001;
        private const uint DISPLAY_DEVICE_PRIMARY = 0x00000004;
        private const uint EDD_GET_DEVICE_INTERFACE_NAME = 0x00000001;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns all active display monitors with their current ICC profile.
        /// </summary>
        public static List<MonitorInfo> GetMonitors()
        {
            var monitors = new List<MonitorInfo>();

            var dev = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            for (uint i = 0; EnumDisplayDevices(null, i, ref dev, 0); i++)
            {
                dev.cb = Marshal.SizeOf<DISPLAY_DEVICE>();
                if ((dev.StateFlags & DISPLAY_DEVICE_ACTIVE) == 0)
                    continue;

                bool isPrimary   = (dev.StateFlags & DISPLAY_DEVICE_PRIMARY) != 0;
                string devName   = dev.DeviceName;
                string devString = dev.DeviceString;

                // Get friendly monitor name by enumerating monitor attached to this adapter
                string friendlyName = GetMonitorFriendlyName(devName, devString);

                // Get current ICC profile via GDI
                string iccPath = GetCurrentIccForDevice(devName);
                string iccName = string.IsNullOrEmpty(iccPath)
                    ? "لا يوجد ملف ICC محدد"
                    : Path.GetFileName(iccPath);

                monitors.Add(new MonitorInfo(
                    DeviceName:    devName,
                    FriendlyName:  friendlyName,
                    CurrentIccPath: iccPath,
                    CurrentIccName: iccName,
                    IsPrimary:     isPrimary
                ));

                dev.cb = Marshal.SizeOf<DISPLAY_DEVICE>();
            }

            return monitors;
        }

        /// <summary>
        /// Gets the ICC profile currently assigned to a specific display device.
        /// Returns empty string if none is assigned.
        /// </summary>
        public static string GetCurrentIccForDevice(string deviceName)
        {
            IntPtr hdc = IntPtr.Zero;
            try
            {
                hdc = CreateDC(null, deviceName, null, IntPtr.Zero);
                if (hdc == IntPtr.Zero) return string.Empty;

                uint size = 512;
                var sb = new StringBuilder(512);
                if (GetICMProfile(hdc, ref size, sb))
                    return sb.ToString();
            }
            catch { /* not fatal */ }
            finally
            {
                if (hdc != IntPtr.Zero) DeleteDC(hdc);
            }
            return string.Empty;
        }

        /// <summary>
        /// Installs an ICC/ICM file and associates it with the specified monitor.
        /// Tries the per-user (no-admin) path first via Windows Color System; only
        /// falls back to the system-wide path (which needs admin) if that fails.
        /// </summary>
        public static (bool Success, string Message) InstallAndApply(
            string iccFilePath, string deviceName)
        {
            if (!File.Exists(iccFilePath))
                return (false, $"ملف ICC غير موجود: {iccFilePath}");

            // 1. Preferred: per-user association (no administrator rights required).
            var perUser = TryApplyPerUser(iccFilePath, deviceName);
            if (perUser.Success)
                return perUser;

            // 2. Fallback: system-wide install (requires administrator rights).
            return TryApplySystemWide(iccFilePath, deviceName);
        }

        /// <summary>
        /// Per-user ICC association via WCS. Copies the profile to a user-writable
        /// color store and associates it for the current user only — no admin needed.
        /// </summary>
        private static (bool Success, string Message) TryApplyPerUser(
            string iccFilePath, string deviceName)
        {
            try
            {
                // Copy to a user-writable color store (LocalAppData) — never System32.
                var userColorDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ApexPrintingSystem", "color");
                Directory.CreateDirectory(userColorDir);

                string destPath = Path.Combine(userColorDir, Path.GetFileName(iccFilePath));
                File.Copy(iccFilePath, destPath, overwrite: true);

                // Enable per-user profiles for this monitor (writes to HKCU).
                WcsSetUsePerUserProfiles(deviceName, CLASS_MONITOR, true);

                // Associate the profile for the current user only.
                bool associated = WcsAssociateColorProfileWithDevice(
                    WCS_PROFILE_MANAGEMENT_SCOPE.CurrentUser, destPath, deviceName);

                if (associated)
                    return (true,
                        $"✅ تم تطبيق {Path.GetFileName(iccFilePath)} على {deviceName} " +
                        $"للمستخدم الحالي (بدون صلاحيات مسؤول).");

                return (false, string.Empty);   // signal caller to try system-wide
            }
            catch
            {
                // Any failure → let the system-wide fallback try next.
                return (false, string.Empty);
            }
        }

        /// <summary>
        /// System-wide ICC install: copies to System32 color folder and registers
        /// machine-wide. Requires administrator rights.
        /// </summary>
        private static (bool Success, string Message) TryApplySystemWide(
            string iccFilePath, string deviceName)
        {
            try
            {
                var colorDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "spool", "drivers", "color");
                Directory.CreateDirectory(colorDir);

                string destPath = Path.Combine(colorDir, Path.GetFileName(iccFilePath));
                File.Copy(iccFilePath, destPath, overwrite: true);

                bool installed = InstallColorProfile(null, destPath);
                if (!installed)
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err != 183) // 183 = ERROR_ALREADY_EXISTS — not a real error
                        return (false, $"فشل تثبيت ملف ICC  (خطأ Win32: {err})");
                }

                bool associated = AssociateColorProfileWithDevice(null, destPath, deviceName);
                if (!associated)
                {
                    int err = Marshal.GetLastWin32Error();
                    return (false,
                        $"تم تثبيت الملف لكن فشل ربطه بالشاشة (خطأ Win32: {err}).");
                }

                return (true,
                    $"✅ تم تطبيق {Path.GetFileName(iccFilePath)} على {deviceName} على مستوى النظام.");
            }
            catch (UnauthorizedAccessException)
            {
                return (false,
                    "تعذّر التطبيق على مستوى النظام (يتطلب صلاحيات مسؤول)، " +
                    "كما تعذّر التطبيق للمستخدم الحالي. جرّب تشغيل البرنامج كمسؤول.");
            }
            catch (Exception ex)
            {
                return (false, $"خطأ غير متوقع: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns the Windows System Color folder path (where ICC files are stored).
        /// </summary>
        public static string GetSystemColorFolder() =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "spool", "drivers", "color");

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string GetMonitorFriendlyName(string adapterName, string adapterDescription)
        {
            // Try to get the monitor device name attached to this adapter
            var mon = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (EnumDisplayDevices(adapterName, 0, ref mon, EDD_GET_DEVICE_INTERFACE_NAME))
            {
                var friendly = mon.DeviceString?.Trim();
                if (!string.IsNullOrWhiteSpace(friendly) &&
                    !friendly.Equals("Generic PnP Monitor", StringComparison.OrdinalIgnoreCase))
                    return friendly;
            }

            // Fallback to adapter name
            return adapterDescription?.Trim() ?? adapterName;
        }
    }
}
