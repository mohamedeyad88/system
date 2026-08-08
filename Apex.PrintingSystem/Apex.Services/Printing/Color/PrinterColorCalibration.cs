using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Apex.Services.Printing.Color
{
    // ─────────────────────────────────────────────────────────────────────────
    // Data model
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Per-printer colour adjustment offsets persisted to JSON.</summary>
    public class PrinterCalibrationProfile
    {
        public string PrinterName { get; set; } = "";
        public DateTime CalibratedAt { get; set; } = DateTime.Now;
        public double BrightnessOffset { get; set; } = 0.0;   // -1.0 to +1.0
        public double ContrastOffset { get; set; } = 0.0;   // -1.0 to +1.0
        public double SaturationOffset { get; set; } = 0.0;   // -1.0 to +1.0
        public double CyanOffset { get; set; } = 0.0;   // CMYK fine-tuning
        public double MagentaOffset { get; set; } = 0.0;
        public double YellowOffset { get; set; } = 0.0;
        public double BlackOffset { get; set; } = 0.0;
        public string? IccProfilePath { get; set; }
        public string Notes { get; set; } = "";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Service
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Singleton service that stores per-printer colour adjustment offsets,
    /// persists them to JSON, and applies them to bitmaps.
    /// </summary>
    public sealed class PrinterColorCalibration
    {
        // ── Singleton ─────────────────────────────────────────────────────────

        private static readonly Lazy<PrinterColorCalibration> _instance =
            new(() => new PrinterColorCalibration(),
                System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

        public static PrinterColorCalibration Instance => _instance.Value;

        // ── Fields ────────────────────────────────────────────────────────────

        private readonly string _calibrationFilePath;
        private readonly ConcurrentDictionary<string, PrinterCalibrationProfile> _calibrations;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        // ── Constructor ───────────────────────────────────────────────────────

        private PrinterColorCalibration()
        {
            string appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Apex");

            _calibrationFilePath = Path.Combine(appDataDir, "ColorCalibrations.json");
            _calibrations = new ConcurrentDictionary<string, PrinterCalibrationProfile>(
                StringComparer.OrdinalIgnoreCase);

            LoadFromDisk();
        }

        // ── Persistence ───────────────────────────────────────────────────────

        private void LoadFromDisk()
        {
            try
            {
                if (!File.Exists(_calibrationFilePath))
                {
                    Debug.WriteLine($"[PrinterColorCalibration] No calibration file at {_calibrationFilePath}.");
                    return;
                }

                string json = File.ReadAllText(_calibrationFilePath);
                var list = JsonSerializer.Deserialize<List<PrinterCalibrationProfile>>(json, JsonOptions);
                if (list is null) return;

                foreach (var p in list)
                {
                    if (!string.IsNullOrWhiteSpace(p.PrinterName))
                        _calibrations[p.PrinterName] = p;
                }

                Debug.WriteLine($"[PrinterColorCalibration] Loaded {_calibrations.Count} calibration(s).");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PrinterColorCalibration] Load failed: {ex.Message}");
            }
        }

        private void SaveToDisk()
        {
            try
            {
                string dir = Path.GetDirectoryName(_calibrationFilePath)!;
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string json = JsonSerializer.Serialize(_calibrations.Values.ToList(), JsonOptions);
                File.WriteAllText(_calibrationFilePath, json);
                Debug.WriteLine($"[PrinterColorCalibration] Saved {_calibrations.Count} calibration(s) to {_calibrationFilePath}.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PrinterColorCalibration] Save failed: {ex.Message}");
            }
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the calibration profile for <paramref name="printerName"/>.
        /// If none exists, returns a default (zero-offset) profile.
        /// </summary>
        public PrinterCalibrationProfile GetCalibration(string printerName)
        {
            if (_calibrations.TryGetValue(printerName ?? "", out var profile))
                return profile;

            return new PrinterCalibrationProfile { PrinterName = printerName ?? "" };
        }

        /// <summary>Persists the given <paramref name="profile"/> to disk.</summary>
        public void SaveCalibration(PrinterCalibrationProfile profile)
        {
            if (profile is null) throw new ArgumentNullException(nameof(profile));
            if (string.IsNullOrWhiteSpace(profile.PrinterName))
                throw new ArgumentException("PrinterName must not be empty.", nameof(profile));

            profile.CalibratedAt = DateTime.Now;
            _calibrations[profile.PrinterName] = profile;
            SaveToDisk();
        }

        /// <summary>Removes calibration for <paramref name="printerName"/> and persists.</summary>
        public void ResetCalibration(string printerName)
        {
            _calibrations.TryRemove(printerName ?? "", out _);
            SaveToDisk();
            Debug.WriteLine($"[PrinterColorCalibration] Reset calibration for '{printerName}'.");
        }

        /// <summary>Returns a snapshot of all stored calibrations.</summary>
        public IReadOnlyList<PrinterCalibrationProfile> GetAllCalibrations() =>
            _calibrations.Values.ToList();

        // ── Bitmap Calibration ────────────────────────────────────────────────

        /// <summary>
        /// Applies the stored calibration for <paramref name="printerName"/> to
        /// <paramref name="source"/> and returns the adjusted bitmap.
        /// Processes brightness → contrast → saturation → CMYK offsets in order.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public unsafe Bitmap ApplyCalibration(Bitmap source, string printerName)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));

            var cal = GetCalibration(printerName);

            // Short-circuit when all offsets are zero
            bool isIdentity =
                cal.BrightnessOffset == 0.0 &&
                cal.ContrastOffset == 0.0 &&
                cal.SaturationOffset == 0.0 &&
                cal.CyanOffset == 0.0 &&
                cal.MagentaOffset == 0.0 &&
                cal.YellowOffset == 0.0 &&
                cal.BlackOffset == 0.0;

            if (isIdentity) return new Bitmap(source);

            int width = source.Width;
            int height = source.Height;

            var result = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            var rect = new Rectangle(0, 0, width, height);

            var srcData = source.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            var dstData = result.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

            try
            {
                byte* srcPtr = (byte*)srcData.Scan0;
                byte* dstPtr = (byte*)dstData.Scan0;

                double brightness = cal.BrightnessOffset;    // [-1, +1]
                double contrast = cal.ContrastOffset;       // [-1, +1]
                double saturation = cal.SaturationOffset;     // [-1, +1]
                double cOff = cal.CyanOffset;
                double mOff = cal.MagentaOffset;
                double yOff = cal.YellowOffset;
                double kOff = cal.BlackOffset;

                for (int row = 0; row < height; row++)
                {
                    byte* srcRow = srcPtr + row * srcData.Stride;
                    byte* dstRow = dstPtr + row * dstData.Stride;

                    for (int col = 0; col < width; col++)
                    {
                        int offset = col * 3;

                        // BGR → double [0,1]
                        double b = srcRow[offset] / 255.0;
                        double g = srcRow[offset + 1] / 255.0;
                        double r = srcRow[offset + 2] / 255.0;

                        // 1. Brightness  (additive)
                        r += brightness;
                        g += brightness;
                        b += brightness;

                        // 2. Contrast  (around 0.5)
                        r = (r - 0.5) * (1.0 + contrast) + 0.5;
                        g = (g - 0.5) * (1.0 + contrast) + 0.5;
                        b = (b - 0.5) * (1.0 + contrast) + 0.5;

                        // Clamp after brightness/contrast before saturation
                        r = Clamp01(r);
                        g = Clamp01(g);
                        b = Clamp01(b);

                        // 3. Saturation (via HSL)
                        if (saturation != 0.0)
                        {
                            RgbToHsl(r, g, b, out double h, out double s, out double l);
                            s = Clamp01(s * (1.0 + saturation));
                            HslToRgb(h, s, l, out r, out g, out b);
                        }

                        // 4. CMYK offsets
                        if (cOff != 0.0 || mOff != 0.0 || yOff != 0.0 || kOff != 0.0)
                        {
                            byte rb = ToByte(r), gb = ToByte(g), bb = ToByte(b);
                            var (C, M, Y, K) = ColorTransformEngine.RgbToCmyk(rb, gb, bb);

                            double cD = Clamp01(C / 255.0 + cOff);
                            double mD = Clamp01(M / 255.0 + mOff);
                            double yD = Clamp01(Y / 255.0 + yOff);
                            double kD = Clamp01(K / 255.0 + kOff);

                            // CMYK → RGB for storage
                            r = (1.0 - cD) * (1.0 - kD);
                            g = (1.0 - mD) * (1.0 - kD);
                            b = (1.0 - yD) * (1.0 - kD);
                        }

                        dstRow[offset] = ToByte(b);
                        dstRow[offset + 1] = ToByte(g);
                        dstRow[offset + 2] = ToByte(r);
                    }
                }
            }
            finally
            {
                source.UnlockBits(srcData);
                result.UnlockBits(dstData);
            }

            return result;
        }

        // ── HSL helpers ───────────────────────────────────────────────────────

        private static void RgbToHsl(
            double r, double g, double b,
            out double h, out double s, out double l)
        {
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;

            l = (max + min) / 2.0;

            if (delta < 1e-10)
            {
                h = 0.0;
                s = 0.0;
                return;
            }

            s = l > 0.5
                ? delta / (2.0 - max - min)
                : delta / (max + min);

            if (max == r)
                h = (g - b) / delta + (g < b ? 6.0 : 0.0);
            else if (max == g)
                h = (b - r) / delta + 2.0;
            else
                h = (r - g) / delta + 4.0;

            h /= 6.0;
        }

        private static void HslToRgb(
            double h, double s, double l,
            out double r, out double g, out double b)
        {
            if (s < 1e-10)
            {
                r = g = b = l;
                return;
            }

            double q = l < 0.5 ? l * (1.0 + s) : l + s - l * s;
            double p = 2.0 * l - q;

            r = HslHue(p, q, h + 1.0 / 3.0);
            g = HslHue(p, q, h);
            b = HslHue(p, q, h - 1.0 / 3.0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double HslHue(double p, double q, double t)
        {
            if (t < 0.0) t += 1.0;
            if (t > 1.0) t -= 1.0;
            if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
            return p;
        }

        // ── Pixel helpers ─────────────────────────────────────────────────────

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double Clamp01(double v) => v < 0.0 ? 0.0 : v > 1.0 ? 1.0 : v;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte ToByte(double v) =>
            (byte)Math.Round(Clamp01(v) * 255.0);
    }
}
