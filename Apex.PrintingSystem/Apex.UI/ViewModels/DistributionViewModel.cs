using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows;
using Apex.Services.Printing;
using Apex.Core.Models;
using Apex.Core.Interfaces;
using Microsoft.Win32;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;

namespace Apex.UI.ViewModels
{
    public partial class DistributionViewModel : ViewModelBase
    {
        private readonly IPrinterDiscoveryService _discoveryService;
        private readonly PrintDispatcher _dispatcher;
        private readonly PrinterStatusService _statusService;

        // Step 1: File Selection
        [ObservableProperty] private string? _selectedFilePath;
        [ObservableProperty] private string _fileSize = "0 KB";
        [ObservableProperty] private string _fileType = "Unknown";

        // Step 2: Printer Selection
        [ObservableProperty] private ObservableCollection<PrinterSelectionItem> _printers = new();
        [ObservableProperty] private bool _selectAll;

        // Step 3: Settings
        [ObservableProperty] private DistributionSettings _settings = new();

        // Step 4: Dashboard
        [ObservableProperty] private ObservableCollection<DistributionJob> _jobs = new();
        [ObservableProperty] private int _successCount;
        [ObservableProperty] private int _failedCount;
        [ObservableProperty] private bool _isDistributing;
        [ObservableProperty] private int _currentStep = 1; // 1=File, 2=Printers, 3=Settings, 4=Dashboard
        [ObservableProperty] private double _progressPercent;

        public DistributionViewModel(
            IPrinterDiscoveryService discoveryService,
            PrintDispatcher dispatcher,
            PrinterStatusService statusService)
        {
            _discoveryService = discoveryService;
            _dispatcher = dispatcher;
            _statusService = statusService;

            _dispatcher.OnJobStatusChanged += (s, job) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var existing = Jobs.FirstOrDefault(j => j.PrinterName == job.PrinterName);
                    if (existing != null)
                    {
                        var index = Jobs.IndexOf(existing);
                        Jobs[index] = job; // Trigger update
                    }
                    UpdateStats();
                });
            };


        }

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
             await LoadPrinters();
        }

        private async Task LoadPrinters()
        {
            var list = await _discoveryService.ScanAsync();
            Printers = new ObservableCollection<PrinterSelectionItem>(
                list.Select(p => new PrinterSelectionItem { Info = p, IsSelected = false })
            );
        }

        [RelayCommand]
        private void SelectFile()
        {
            var dialog = new OpenFileDialog();
            if (dialog.ShowDialog() == true)
            {
                SetFileInfo(dialog.FileName);
            }
        }

        /// <summary>
        /// Set file from drag-and-drop
        /// </summary>
        public void SetDroppedFile(string filePath)
        {
            SetFileInfo(filePath);
        }

        private void SetFileInfo(string filePath)
        {
            SelectedFilePath = filePath;
            var fi = new System.IO.FileInfo(filePath);
            FileSize = $"{fi.Length / 1024} KB";
            FileType = fi.Extension.ToUpper();
        }

        [RelayCommand]
        private void ToggleSelectAll()
        {
            SelectAll = !SelectAll;
            foreach (var p in Printers) p.IsSelected = SelectAll;
        }

        [RelayCommand]
        private void NextStep()
        {
            if (CurrentStep == 1 && string.IsNullOrEmpty(SelectedFilePath))
            {
                MessageBox.Show(Services.LocalizationService.Instance.GetString("PleaseSelectFile"));
                return;
            }
            if (CurrentStep == 2 && !Printers.Any(p => p.IsSelected))
            {
                MessageBox.Show(Services.LocalizationService.Instance.GetString("PleaseSelectAtLeastOnePrinter"));
                return;
            }

            if (CurrentStep < 4) CurrentStep++;
        }

        [RelayCommand]
        private void PrevStep()
        {
            if (CurrentStep > 1) CurrentStep--;
        }

        [RelayCommand]
        private async Task StartDistribution()
        {
            IsDistributing = true;
            CurrentStep = 4; // Go to Dashboard
            Jobs.Clear();
            SuccessCount = 0;
            FailedCount = 0;
            ProgressPercent = 0;

            var selectedPrinters = Printers.Where(p => p.IsSelected).Select(p => p.Info.Name).ToList();
            
            // Initialize jobs list for UI immediately
            foreach(var p in selectedPrinters)
            {
                Jobs.Add(new DistributionJob { PrinterName = p, Status = "Pending" });
            }

            await _dispatcher.DispatchAsync(SelectedFilePath!, selectedPrinters, Settings);
            
            IsDistributing = false;
            MessageBox.Show(Services.LocalizationService.Instance.GetString("DistributionComplete"));
        }

        private void UpdateStats()
        {
            SuccessCount = Jobs.Count(j => j.Status == "Completed");
            FailedCount = Jobs.Count(j => j.Status == "Failed");
            
            // Calculate progress
            int totalProcessed = SuccessCount + FailedCount;
            ProgressPercent = Jobs.Count > 0 ? (double)totalProcessed / Jobs.Count * 100 : 0;
        }
    }

    public class PrinterSelectionItem : ObservableObject
    {
        public PrinterInfo Info { get; set; } = new();
        
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }
}
