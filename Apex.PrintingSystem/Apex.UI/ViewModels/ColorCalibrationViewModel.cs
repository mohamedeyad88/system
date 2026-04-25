using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Apex.Core.Interfaces;
using Apex.Services.Printing.Color;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Linq;

namespace Apex.UI.ViewModels
{
    public partial class ColorCalibrationViewModel : ViewModelBase
    {
        private readonly IPrinterDiscoveryService _printerDiscovery;
        private readonly PrinterColorCalibration  _calibration = PrinterColorCalibration.Instance;

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

        // ── Status ────────────────────────────────────────────────────────────
        [ObservableProperty] private string  _statusMessage = "";
        [ObservableProperty] private string? _iccProfilePath;
        [ObservableProperty] private bool    _hasPrinterSelected;

        public ColorCalibrationViewModel(IPrinterDiscoveryService printerDiscovery)
        {
            _printerDiscovery = printerDiscovery;
        }

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            await LoadPrintersAsync();
        }

        // ── Partial hooks ─────────────────────────────────────────────────────

        partial void OnSelectedPrinterNameChanged(string? value)
        {
            HasPrinterSelected = !string.IsNullOrEmpty(value);
            if (string.IsNullOrEmpty(value)) return;

            var profile = _calibration.GetCalibration(value);
            Brightness    = profile.BrightnessOffset;
            Contrast      = profile.ContrastOffset;
            Saturation    = profile.SaturationOffset;
            CyanOffset    = profile.CyanOffset;
            MagentaOffset = profile.MagentaOffset;
            YellowOffset  = profile.YellowOffset;
            BlackOffset   = profile.BlackOffset;
            IccProfilePath = profile.IccProfilePath;

            StatusMessage = "";
        }

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

                    // Auto-select first
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
                PrinterName      = SelectedPrinterName,
                BrightnessOffset = Brightness,
                ContrastOffset   = Contrast,
                SaturationOffset = Saturation,
                CyanOffset       = CyanOffset,
                MagentaOffset    = MagentaOffset,
                YellowOffset     = YellowOffset,
                BlackOffset      = BlackOffset,
                IccProfilePath   = IccProfilePath,
            };

            _calibration.SaveCalibration(profile);
            StatusMessage = "✅ تم حفظ الإعدادات بنجاح";
        }

        [RelayCommand]
        private void ResetCalibration()
        {
            if (string.IsNullOrEmpty(SelectedPrinterName)) return;

            _calibration.ResetCalibration(SelectedPrinterName);

            Brightness    = 0;
            Contrast      = 0;
            Saturation    = 0;
            CyanOffset    = 0;
            MagentaOffset = 0;
            YellowOffset  = 0;
            BlackOffset   = 0;

            StatusMessage = "↺ تم إعادة الضبط للقيم الافتراضية";
        }
    }
}
