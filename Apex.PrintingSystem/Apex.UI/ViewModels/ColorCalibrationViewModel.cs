using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Apex.Core.Interfaces;
using Apex.Services.Printing.Color;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Apex.UI.ViewModels
{
    public partial class ColorCalibrationViewModel : ViewModelBase
    {
        private readonly IPrinterDiscoveryService _printerDiscovery;
        private readonly PrinterColorCalibration _calibration = PrinterColorCalibration.Instance;
        private readonly IccProfileManager _icc = IccProfileManager.Instance;

        // ── Printer list ──────────────────────────────────────────────────────
        [ObservableProperty] private ObservableCollection<string> _printerNames = new();
        [ObservableProperty] private string? _selectedPrinterName;

        // ── Calibration sliders ───────────────────────────────────────────────
        [ObservableProperty] private double _brightness;
        [ObservableProperty] private double _contrast;
        [ObservableProperty] private double _saturation;
        [ObservableProperty] private double _cyanOffset;
        [ObservableProperty] private double _magentaOffset;
        [ObservableProperty] private double _yellowOffset;
        [ObservableProperty] private double _blackOffset;

        // ── Notes & metadata ──────────────────────────────────────────────────
        [ObservableProperty] private string _notes = "";
        [ObservableProperty] private string _calibratedAtText = "—";

        // ── Status ────────────────────────────────────────────────────────────
        [ObservableProperty] private string _statusMessage = "";
        [ObservableProperty] private string? _iccProfilePath;
        [ObservableProperty] private string _iccProfileName = "—";
        [ObservableProperty] private bool _hasPrinterSelected;
        [ObservableProperty] private bool _hasIccProfile;

        // ── Color preview swatches ────────────────────────────────────────────
        // Six reference colors shown as before→after calibration strips
        [ObservableProperty] private Color _swatchWhite = Colors.White;
        [ObservableProperty] private Color _swatchRed = Color.FromRgb(220, 50, 50);
        [ObservableProperty] private Color _swatchGreen = Color.FromRgb(50, 180, 50);
        [ObservableProperty] private Color _swatchBlue = Color.FromRgb(50, 100, 220);
        [ObservableProperty] private Color _swatchGray = Color.FromRgb(128, 128, 128);
        [ObservableProperty] private Color _swatchBlack = Color.FromRgb(20, 20, 20);

        // ── ICC Profiles list (for current printer) ───────────────────────────
        [ObservableProperty] private ObservableCollection<IccProfile> _availableIccProfiles = new();

        // ── Colour management for the preview ─────────────────────────────────
        // The swatches are what the operator judges the sliders against, so they have
        // to be separated the way the press will separate them.
        private IccLookupTable? _colorLookup;

        /// <summary>Plain-language note about which profile the preview is using.</summary>
        [ObservableProperty] private string _colorPathNote = "";

        private void RefreshColorManagement(string? printerName)
        {
            var colour = ColorManagementService.Instance;
            var resolution = colour.Resolve(printerName);
            _colorLookup = colour.CreateLookup(printerName);

            ColorPathNote = _colorLookup == null
                ? "المعاينة بالمعادلة المدمجة — لا يوجد ملف ICC مناسب"
                : resolution.Path == ColorPath.PrinterProfile
                    ? $"المعاينة عبر ملف الطابعة: {System.IO.Path.GetFileNameWithoutExtension(resolution.ProfilePath)}"
                    : $"المعاينة عبر معيار الدار: {System.IO.Path.GetFileNameWithoutExtension(resolution.ProfilePath)}";

            RefreshSwatches();
        }

        // ── Display / Monitor ICC section ─────────────────────────────────────
        [ObservableProperty] private ObservableCollection<MonitorInfo> _monitors = new();
        [ObservableProperty] private MonitorInfo? _selectedMonitor;
        [ObservableProperty] private string _displayIccStatus = "";
        [ObservableProperty] private string _importedIccPath = "";
        [ObservableProperty] private string _importedIccName = "اختر ملف ICC للشاشة...";

        public ColorCalibrationViewModel(IPrinterDiscoveryService printerDiscovery)
        {
            _printerDiscovery = printerDiscovery;
        }

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            await LoadPrintersAsync();
            await Task.Run(DetectMonitors);
        }

        partial void OnSelectedMonitorChanged(MonitorInfo? value)
        {
            DisplayIccStatus = value is null ? "" :
                string.IsNullOrEmpty(value.CurrentIccPath)
                    ? "⚠️ لا يوجد ملف ICC محدد لهذه الشاشة"
                    : $"الملف الحالي: {value.CurrentIccName}";
        }

        // ── Partial hooks ─────────────────────────────────────────────────────

        partial void OnSelectedPrinterNameChanged(string? value)
        {
            HasPrinterSelected = !string.IsNullOrEmpty(value);

            // Rebuild the preview's colour path for the newly selected device.
            RefreshColorManagement(value);

            if (string.IsNullOrEmpty(value)) return;

            // Load calibration profile
            var profile = _calibration.GetCalibration(value);
            Brightness = profile.BrightnessOffset;
            Contrast = profile.ContrastOffset;
            Saturation = profile.SaturationOffset;
            CyanOffset = profile.CyanOffset;
            MagentaOffset = profile.MagentaOffset;
            YellowOffset = profile.YellowOffset;
            BlackOffset = profile.BlackOffset;
            Notes = profile.Notes;
            IccProfilePath = profile.IccProfilePath;
            CalibratedAtText = profile.CalibratedAt == default
                ? "—"
                : profile.CalibratedAt.ToString("yyyy-MM-dd  HH:mm");

            // ICC info
            UpdateIccInfo(profile.IccProfilePath);

            // Load ICC profiles for this printer
            var iccProfiles = _icc.GetAllProfiles()
                .Where(p => p.DeviceClass == "prtr" || p.DeviceClass == "Unknown")
                .ToList();
            AvailableIccProfiles.Clear();
            foreach (var p in iccProfiles)
                AvailableIccProfiles.Add(p);

            StatusMessage = "";
            RefreshSwatches();
        }

        // When any slider changes, refresh the preview swatches
        partial void OnBrightnessChanged(double value) => RefreshSwatches();
        partial void OnContrastChanged(double value) => RefreshSwatches();
        partial void OnSaturationChanged(double value) => RefreshSwatches();
        partial void OnCyanOffsetChanged(double value) => RefreshSwatches();
        partial void OnMagentaOffsetChanged(double value) => RefreshSwatches();
        partial void OnYellowOffsetChanged(double value) => RefreshSwatches();
        partial void OnBlackOffsetChanged(double value) => RefreshSwatches();

        // ── Commands ──────────────────────────────────────────────────────────

        [RelayCommand]
        private async Task LoadPrintersAsync()
        {
            try
            {
                var printers = await _printerDiscovery.ScanAsync();
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    PrinterNames.Clear();
                    foreach (var p in printers)
                        PrinterNames.Add(p.Name);

                    if (PrinterNames.Any() && SelectedPrinterName == null)
                        SelectedPrinterName = PrinterNames.First();
                });
            }
            catch { /* best-effort */ }
        }

        [RelayCommand]
        private void SaveCalibration()
        {
            if (string.IsNullOrEmpty(SelectedPrinterName)) return;

            var profile = new PrinterCalibrationProfile
            {
                PrinterName = SelectedPrinterName,
                BrightnessOffset = Brightness,
                ContrastOffset = Contrast,
                SaturationOffset = Saturation,
                CyanOffset = CyanOffset,
                MagentaOffset = MagentaOffset,
                YellowOffset = YellowOffset,
                BlackOffset = BlackOffset,
                IccProfilePath = IccProfilePath,
                Notes = Notes,
            };

            _calibration.SaveCalibration(profile);
            CalibratedAtText = DateTime.Now.ToString("yyyy-MM-dd  HH:mm");
            StatusMessage = "✅ تم حفظ الإعدادات بنجاح";
        }

        [RelayCommand]
        private void ResetCalibration()
        {
            if (string.IsNullOrEmpty(SelectedPrinterName)) return;

            _calibration.ResetCalibration(SelectedPrinterName);

            Brightness = 0; Contrast = 0; Saturation = 0;
            CyanOffset = 0; MagentaOffset = 0; YellowOffset = 0;
            BlackOffset = 0; Notes = "";
            IccProfilePath = null;
            CalibratedAtText = "—";
            UpdateIccInfo(null);

            StatusMessage = "↺ تم إعادة الضبط للقيم الافتراضية";
        }

        [RelayCommand]
        private void BrowseIccProfile()
        {
            var dlg = new OpenFileDialog
            {
                Title = "اختر ملف ICC / ICM",
                Filter = "ICC Profiles|*.icc;*.icm|All Files|*.*",
            };
            if (dlg.ShowDialog() == true)
            {
                IccProfilePath = dlg.FileName;
                UpdateIccInfo(dlg.FileName);
                StatusMessage = "";
            }
        }

        [RelayCommand]
        private void AutoDetectIcc()
        {
            if (string.IsNullOrEmpty(SelectedPrinterName)) return;

            var profile = _icc.GetProfileForPrinter(SelectedPrinterName);
            if (profile is not null)
            {
                IccProfilePath = profile.FilePath;
                UpdateIccInfo(profile.FilePath);
                StatusMessage = $"✅ ICC: {profile.ProfileName}";
            }
            else
            {
                StatusMessage = "⚠️ لم يتم العثور على ملف ICC للطابعة";
            }
        }

        [RelayCommand]
        private void ClearIccProfile()
        {
            IccProfilePath = null;
            UpdateIccInfo(null);
        }

        // ── Display ICC commands ───────────────────────────────────────────────

        [RelayCommand]
        private void DetectMonitors()
        {
            try
            {
                var list = DisplayIccDetector.GetMonitors();
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Monitors.Clear();
                    foreach (var m in list) Monitors.Add(m);
                    SelectedMonitor = Monitors.FirstOrDefault(m => m.IsPrimary)
                                   ?? Monitors.FirstOrDefault();
                });
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
                    DisplayIccStatus = $"⚠️ تعذّر الكشف عن الشاشات: {ex.Message}");
            }
        }

        [RelayCommand]
        private void BrowseDisplayIcc()
        {
            var dlg = new OpenFileDialog
            {
                Title       = "اختر ملف ICC / ICM للشاشة",
                Filter      = "ICC Profiles|*.icc;*.icm|All Files|*.*",
                InitialDirectory = DisplayIccDetector.GetSystemColorFolder()
            };
            if (dlg.ShowDialog() == true)
            {
                ImportedIccPath = dlg.FileName;
                ImportedIccName = System.IO.Path.GetFileName(dlg.FileName);
                DisplayIccStatus = "";
            }
        }

        [RelayCommand]
        private async Task ApplyDisplayIcc()
        {
            if (SelectedMonitor is null)
            {
                DisplayIccStatus = "⚠️ اختر شاشة أولاً";
                return;
            }
            if (string.IsNullOrEmpty(ImportedIccPath))
            {
                DisplayIccStatus = "⚠️ اختر ملف ICC أولاً";
                return;
            }

            // Run the slow Win32 install/associate work off the UI thread so the
            // window stays responsive (file copy to System32 + mscms.dll calls can
            // block for seconds). The async RelayCommand also disables itself while
            // running, preventing duplicate clicks.
            DisplayIccStatus = "⏳ جارٍ تطبيق ملف ICC على الشاشة…";

            string iccPath = ImportedIccPath;
            string deviceName = SelectedMonitor.DeviceName;

            var (success, message) = await Task.Run(
                () => DisplayIccDetector.InstallAndApply(iccPath, deviceName));

            // Back on the UI thread after await — safe to touch bound properties.
            DisplayIccStatus = message;

            if (success)
            {
                // Refresh monitor list (Win32 enumeration) off-thread too.
                await Task.Run(DetectMonitors);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void UpdateIccInfo(string? path)
        {
            if (string.IsNullOrEmpty(path))
            {
                IccProfileName = "—";
                HasIccProfile = false;
                return;
            }

            HasIccProfile = true;
            // Try to get name from the manager
            var known = _icc.GetAllProfiles()
                .FirstOrDefault(p => string.Equals(p.FilePath, path, StringComparison.OrdinalIgnoreCase));
            IccProfileName = known?.ProfileName ?? System.IO.Path.GetFileNameWithoutExtension(path);
        }

        /// <summary>
        /// Applies the current slider values to 6 reference colors
        /// and updates the swatch properties so the XAML preview reacts live.
        /// </summary>
        private void RefreshSwatches()
        {
            SwatchWhite = Calibrate(255, 255, 255);
            SwatchRed = Calibrate(220, 50, 50);
            SwatchGreen = Calibrate(50, 180, 50);
            SwatchBlue = Calibrate(50, 100, 220);
            SwatchGray = Calibrate(128, 128, 128);
            SwatchBlack = Calibrate(20, 20, 20);
        }

        /// <summary>Applies the in-memory slider offsets to a single pixel.</summary>
        private Color Calibrate(byte r8, byte g8, byte b8)
        {
            // Replicate the same pipeline as PrinterColorCalibration.ApplyCalibration()
            double r = r8 / 255.0;
            double g = g8 / 255.0;
            double b = b8 / 255.0;

            // 1. Brightness
            r += Brightness; g += Brightness; b += Brightness;

            // 2. Contrast (around 0.5)
            r = (r - 0.5) * (1.0 + Contrast) + 0.5;
            g = (g - 0.5) * (1.0 + Contrast) + 0.5;
            b = (b - 0.5) * (1.0 + Contrast) + 0.5;

            r = Clamp01(r); g = Clamp01(g); b = Clamp01(b);

            // 3. Saturation (via HSL)
            if (Saturation != 0.0)
            {
                RgbToHsl(r, g, b, out double h, out double s, out double l);
                s = Clamp01(s * (1.0 + Saturation));
                HslToRgb(h, s, l, out r, out g, out b);
            }

            // 4. CMYK offsets
            if (CyanOffset != 0.0 || MagentaOffset != 0.0 || YellowOffset != 0.0 || BlackOffset != 0.0)
            {
                byte rb = ToByte(r), gb = ToByte(g), bb = ToByte(b);

                // Separate through the selected printer's press profile when there is
                // one. A preview built from the generic formula shows the same colour
                // for coated stock and newsprint, so the operator would be adjusting
                // sliders against a picture the press cannot reproduce.
                var (C, M, Y, K) = _colorLookup != null
                    ? _colorLookup.RgbToCmyk(rb, gb, bb)
                    : ColorTransformEngine.RgbToCmyk(rb, gb, bb);

                double cD = Clamp01(C / 255.0 + CyanOffset);
                double mD = Clamp01(M / 255.0 + MagentaOffset);
                double yD = Clamp01(Y / 255.0 + YellowOffset);
                double kD = Clamp01(K / 255.0 + BlackOffset);

                r = (1.0 - cD) * (1.0 - kD);
                g = (1.0 - mD) * (1.0 - kD);
                b = (1.0 - yD) * (1.0 - kD);
            }

            return Color.FromRgb(ToByte(r), ToByte(g), ToByte(b));
        }

        // ── Pixel math (mirrors PrinterColorCalibration) ─────────────────────

        private static double Clamp01(double v) => v < 0.0 ? 0.0 : v > 1.0 ? 1.0 : v;
        private static byte ToByte(double v) => (byte)Math.Round(Clamp01(v) * 255.0);

        private static void RgbToHsl(double r, double g, double b,
                                     out double h, out double s, out double l)
        {
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;
            l = (max + min) / 2.0;
            if (delta < 1e-10) { h = 0.0; s = 0.0; return; }
            s = l > 0.5 ? delta / (2.0 - max - min) : delta / (max + min);
            if (max == r) h = (g - b) / delta + (g < b ? 6.0 : 0.0);
            else if (max == g) h = (b - r) / delta + 2.0;
            else h = (r - g) / delta + 4.0;
            h /= 6.0;
        }

        private static void HslToRgb(double h, double s, double l,
                                     out double r, out double g, out double b)
        {
            if (s < 1e-10) { r = g = b = l; return; }
            double q = l < 0.5 ? l * (1.0 + s) : l + s - l * s;
            double p = 2.0 * l - q;
            r = HslHue(p, q, h + 1.0 / 3.0);
            g = HslHue(p, q, h);
            b = HslHue(p, q, h - 1.0 / 3.0);
        }

        private static double HslHue(double p, double q, double t)
        {
            if (t < 0.0) t += 1.0; if (t > 1.0) t -= 1.0;
            if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
            return p;
        }
    }
}
