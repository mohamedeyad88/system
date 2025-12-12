using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows;
using Apex.Services.Printing;
using Apex.Core.Models;
using Apex.Core.Interfaces;
using Apex.Services.Conversion;
using Microsoft.Win32;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.IO;

namespace Apex.UI.ViewModels
{
    /// <summary>
    /// ViewModel for batch print with document conversion support.
    /// Workflow: Upload Files → Select Printer → Start Print
    /// </summary>
    public partial class BatchPrintViewModel : ViewModelBase
    {
        private readonly IPrinterDiscoveryService _discoveryService;
        private readonly BatchPrintJobManager _batchManager;
        private readonly FilePreparationService _fileService;
        private readonly IDocumentConverter _converter;
        private readonly IPrintEngine? _printEngine;

        // File list
        [ObservableProperty] private ObservableCollection<BatchJob> _files = new();
        
        // Printer selection (single printer only)
        [ObservableProperty] private ObservableCollection<string> _printerNames = new();
        [ObservableProperty] private string? _selectedPrinter;

        // Settings
        [ObservableProperty] private BatchSettings _settings = new();

        // State
        [ObservableProperty] private string _batchStatus = "Ready";
        [ObservableProperty] private bool _isPrinting;
        [ObservableProperty] private bool _isPaused;
        [ObservableProperty] private double _progressPercent;
        
        // Stats
        [ObservableProperty] private int _totalJobs;
        [ObservableProperty] private int _pendingJobs;
        [ObservableProperty] private int _convertingJobs;
        [ObservableProperty] private int _printingJobs;
        [ObservableProperty] private int _completedJobs;
        [ObservableProperty] private int _failedJobs;
        public BatchPrintViewModel(
            IPrinterDiscoveryService discoveryService,
            BatchPrintJobManager batchManager,
            FilePreparationService fileService,
            IDocumentConverter? converter = null,
            IPrintEngine? printEngine = null)
        {
            _discoveryService = discoveryService;
            _batchManager = batchManager;
            _fileService = fileService;
            _converter = converter ?? new DocumentConverter();
            _printEngine = printEngine;

            // Wire up event handlers
            _batchManager.OnJobStatusChanged += OnJobStatusChanged;
            _batchManager.OnBatchStatusChanged += (s, status) => BatchStatus = status;
            _batchManager.OnBatchProgressChanged += OnBatchProgressChanged;
        }

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            await LoadPrinters();
        }

        private async Task LoadPrinters()
        {
            var list = await _discoveryService.ScanAsync();
            PrinterNames = new ObservableCollection<string>(list.Select(p => p.Name));
            SelectedPrinter = list.FirstOrDefault(p => p.IsDefault)?.Name ?? PrinterNames.FirstOrDefault();
        }

        private void OnJobStatusChanged(object? sender, BatchJob job)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var existing = Files.FirstOrDefault(f => f.JobId == job.JobId);
                if (existing != null)
                {
                    var idx = Files.IndexOf(existing);
                    Files[idx] = job;
                }
                UpdateStats();
            });
        }

        private void UpdateStats()
        {
            TotalJobs = Files.Count;
            PendingJobs = Files.Count(f => f.Status == "Pending");
            ConvertingJobs = Files.Count(f => f.Status == "Converting");
            PrintingJobs = Files.Count(f => f.Status == "Printing");
            CompletedJobs = Files.Count(f => f.Status == "Completed");
            FailedJobs = Files.Count(f => f.Status == "Failed");
        }

        private void OnBatchProgressChanged(object? sender, BatchProgress progress)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                TotalJobs = progress.TotalJobs;
                PendingJobs = progress.PendingJobs;
                ConvertingJobs = progress.ConvertingJobs;
                PrintingJobs = progress.PrintingJobs;
                CompletedJobs = progress.CompletedJobs;
                FailedJobs = progress.FailedJobs;
                ProgressPercent = progress.PercentComplete;
                
                if (BatchStatus.Contains("completed") || BatchStatus.Contains("cancelled"))
                {
                    IsPrinting = false;
                    IsPaused = false;
                }
            });
        }

        #region File Management Commands

        [RelayCommand]
        private async Task AddFiles()
        {
            var dialog = new OpenFileDialog 
            { 
                Multiselect = true,
                Filter = "All Supported|*.txt;*.csv;*.pdf;*.png;*.jpg;*.jpeg;*.bmp;*.tiff;*.tif;*.html;*.htm;*.doc;*.docx;*.xls;*.xlsx;*.ppt;*.pptx;*.odt;*.ods;*.odp|" +
                         "Documents|*.pdf;*.doc;*.docx;*.txt;*.rtf|" +
                         "Images|*.png;*.jpg;*.jpeg;*.bmp;*.tiff;*.tif|" +
                         "Spreadsheets|*.xls;*.xlsx;*.csv|" +
                         "Presentations|*.ppt;*.pptx|" +
                         "All Files|*.*"
            };
            
            if (dialog.ShowDialog() == true)
            {
                await AddFilesInternal(dialog.FileNames);
            }
        }

        public async Task AddDroppedFilesAsync(string[] filePaths)
        {
            await AddFilesInternal(filePaths);
        }

        private async Task AddFilesInternal(string[] filePaths)
        {
            foreach (var path in filePaths)
            {
                if (!File.Exists(path)) continue;
                
                var extension = Path.GetExtension(path).ToLowerInvariant();
                
                // Check if format is supported
                if (!_converter.IsFormatSupported(extension))
                {
                    var message = Services.LocalizationService.Instance.GetString("UnsupportedFormat") + ": " + extension + "\n" +
                                 Services.LocalizationService.Instance.GetString("File") + ": " + Path.GetFileName(path);
                    MessageBox.Show(message, 
                        Services.LocalizationService.Instance.GetString("UnsupportedFormat"), 
                        MessageBoxButton.OK, 
                        MessageBoxImage.Warning);
                    continue;
                }

                var fileInfo = new FileInfo(path);
                var estimatedPages = await _converter.EstimatePagesAsync(path);

                var job = new BatchJob
                {
                    FilePath = path,
                    FileType = extension.TrimStart('.').ToUpperInvariant(),
                    FileSizeBytes = fileInfo.Length,
                    FileSize = FormatFileSize(fileInfo.Length),
                    EstimatedPages = estimatedPages,
                    ConversionStatus = extension == ".pdf" ? ConversionStatus.Skipped : ConversionStatus.Pending
                };

                Files.Add(job);
            }

            UpdateStats();
        }

        [RelayCommand]
        private void RemoveFile(BatchJob file)
        {
            if (Files.Contains(file)) 
            {
                Files.Remove(file);
                UpdateStats();
            }
        }

        [RelayCommand]
        private void ClearFiles()
        {
            Files.Clear();
            UpdateStats();
        }

        #endregion

        #region Print Commands

        [RelayCommand]
        private async Task StartPrint()
        {
            if (!Files.Any())
            {
                MessageBox.Show(
                    Services.LocalizationService.Instance.GetString("PleaseAddAtLeastOneFile"), 
                    Services.LocalizationService.Instance.GetString("NoFiles"), 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(SelectedPrinter))
            {
                MessageBox.Show(
                    Services.LocalizationService.Instance.GetString("PleaseSelectPrinter"), 
                    Services.LocalizationService.Instance.GetString("NoPrinter"), 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Warning);
                return;
            }

            IsPrinting = true;
            IsPaused = false;
            BatchStatus = "Starting...";

            // Reset all jobs to pending
            foreach (var job in Files)
            {
                job.Status = "Pending";
                job.ConversionStatus = job.OriginalExtension == ".pdf" 
                    ? ConversionStatus.Skipped 
                    : ConversionStatus.Pending;
                job.ErrorMessage = "";
                job.CurrentPage = 0;
            }

            UpdateStats();
            await _batchManager.ProcessBatchAsync(SelectedPrinter, Files.ToList(), Settings);
        }

        [RelayCommand]
        private void PauseResume()
        {
            if (IsPaused)
            {
                _batchManager.Resume();
                IsPaused = false;
            }
            else
            {
                _batchManager.Pause();
                IsPaused = true;
            }
        }

        [RelayCommand]
        private void CancelBatch()
        {
            _batchManager.CancelBatch();
            IsPrinting = false;
            IsPaused = false;
        }

        [RelayCommand]
        private void RetryFailed()
        {
            // Reset failed jobs to pending
            foreach (var job in Files.Where(f => f.Status == "Failed"))
            {
                job.Status = "Pending";
                job.ConversionStatus = ConversionStatus.Pending;
                job.ErrorMessage = "";
                job.RetryCount = 0;
            }
            UpdateStats();
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }

        #endregion
    }
}
