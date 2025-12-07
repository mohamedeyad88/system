using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows;
using Apex.UI.Services.Printing;
using Microsoft.Win32;
using System.IO;
using Apex.Core.Interfaces;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Apex.UI.ViewModels
{
    public partial class PrintManagerViewModel : ViewModelBase
    {
        private readonly BatchPrintService _batchPrintService;
        private readonly IPrinterDiscoveryService _printerService;

        [ObservableProperty]
        private ObservableCollection<string> _availablePrinters = new();

        [ObservableProperty]
        private string? _selectedPrinter;

        [ObservableProperty]
        private ObservableCollection<string> _filesToPrint = new();

        [ObservableProperty]
        private ObservableCollection<IngestedFileItem> _ingestedFiles = new();

        [ObservableProperty]
        private string _statusMessage = "List of documents is empty";

        [ObservableProperty]
        private int _progressValue;

        [ObservableProperty]
        private bool _isPrinting;

        // Print Settings Properties
        [ObservableProperty]
        private bool _isSettingsOpen;

        [ObservableProperty]
        private bool _isDuplexEnabled = false;

        [ObservableProperty]
        private bool _isColorEnabled = true;

        [ObservableProperty]
        private string _printQuality = "Normal";

        [ObservableProperty]
        private string _orientation = "Portrait";

        [ObservableProperty]
        private int _defaultCopies = 1;

        public ObservableCollection<string> PrintQualities { get; } = new() { "Draft", "Normal", "High", "Best" };
        public ObservableCollection<string> Orientations { get; } = new() { "Portrait", "Landscape" };

        public PrintManagerViewModel(BatchPrintService batchPrintService, IPrinterDiscoveryService printerService)
        {
            _batchPrintService = batchPrintService;
            _printerService = printerService;

            _batchPrintService.OnStatusUpdate += (s, e) => StatusMessage = e;
            _batchPrintService.OnProgressUpdate += (s, e) => ProgressValue = e;

            LoadPrinters();
        }

        private async void LoadPrinters()
        {
            var printers = await _printerService.ScanAsync();
            AvailablePrinters = new ObservableCollection<string>(printers.Select(p => p.Name));
            if (AvailablePrinters.Count > 0) SelectedPrinter = AvailablePrinters[0];
        }

        [RelayCommand]
        private void DropFiles(string[] files)
        {
            foreach (var file in files)
            {
                AddFileToList(file);
            }
            UpdateStatus();
        }

        [RelayCommand]
        private void BrowseFiles()
        {
            var dialog = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "All Files|*.*|PDF Files|*.pdf|Images|*.png;*.jpg;*.jpeg|Documents|*.docx;*.xlsx"
            };

            if (dialog.ShowDialog() == true)
            {
                foreach (var file in dialog.FileNames)
                {
                    AddFileToList(file);
                }
                UpdateStatus();
            }
        }

        [RelayCommand]
        private void BrowseFolder()
        {
            // WPF doesn't have native FolderBrowserDialog, use OpenFileDialog with folder mode
            var dialog = new OpenFileDialog
            {
                Title = "Select a folder (select any file in the folder)",
                Filter = "All Files|*.*",
                CheckFileExists = true,
                Multiselect = true
            };

            if (dialog.ShowDialog() == true && dialog.FileNames.Length > 0)
            {
                var folderPath = Path.GetDirectoryName(dialog.FileName);
                if (!string.IsNullOrEmpty(folderPath))
                {
                    var files = Directory.GetFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly);
                    foreach (var file in files)
                    {
                        AddFileToList(file);
                    }
                    UpdateStatus();
                }
            }
        }

        private void AddFileToList(string filePath)
        {
            if (IngestedFiles.Any(f => f.FullPath == filePath)) return;

            var fileInfo = new FileInfo(filePath);
            var item = new IngestedFileItem
            {
                Index = IngestedFiles.Count + 1,
                OriginalName = fileInfo.Name,
                FullPath = filePath,
                FolderPath = fileInfo.DirectoryName ?? "",
                SizeBytes = fileInfo.Length,
                SizeDisplay = FormatFileSize(fileInfo.Length),
                FileType = fileInfo.Extension.TrimStart('.').ToUpper(),
                ModifiedDate = fileInfo.LastWriteTime,
                Copies = 1,
                PageRange = "All"
            };

            IngestedFiles.Add(item);
            FilesToPrint.Add(filePath);
        }

        private string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / 1024.0 / 1024.0:F1} MB";
        }

        private void UpdateStatus()
        {
            StatusMessage = IngestedFiles.Count > 0 
                ? $"{IngestedFiles.Count} document(s) ready" 
                : "List of documents is empty";
        }

        [RelayCommand]
        private void RemoveFile(string filePath)
        {
            var item = IngestedFiles.FirstOrDefault(f => f.FullPath == filePath);
            if (item != null) IngestedFiles.Remove(item);
            if (FilesToPrint.Contains(filePath)) FilesToPrint.Remove(filePath);
            UpdateStatus();
        }

        [RelayCommand]
        private void ClearList()
        {
            IngestedFiles.Clear();
            FilesToPrint.Clear();
            UpdateStatus();
        }

        [RelayCommand]
        private void OpenPrinterProperties()
        {
            if (string.IsNullOrEmpty(SelectedPrinter))
            {
                MessageBox.Show(
                    Services.LocalizationService.Instance.GetString("PleaseSelectPrinterFirst"), 
                    Services.LocalizationService.Instance.GetString("PrinterProperties"), 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Information);
                return;
            }

            try
            {
                // Open Windows printer properties dialog
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "rundll32.exe",
                    Arguments = $"printui.dll,PrintUIEntry /e /n \"{SelectedPrinter}\"",
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                var message = Services.LocalizationService.Instance.GetString("CouldNotOpenPrinterProperties") + ": " + ex.Message;
                MessageBox.Show(message, Services.LocalizationService.Instance.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void OpenSettings()
        {
            IsSettingsOpen = true;
        }

        [RelayCommand]
        private void CloseSettings()
        {
            IsSettingsOpen = false;
        }

        [RelayCommand]
        private void ApplySettings()
        {
            // Apply settings to all files
            foreach (var file in IngestedFiles)
            {
                file.Copies = DefaultCopies;
            }
            IsSettingsOpen = false;
            StatusMessage = "Settings applied successfully";
        }

        [RelayCommand]
        private void Refresh()
        {
            LoadPrinters();
        }

        [RelayCommand]
        private async Task CreateJobs()
        {
            if (string.IsNullOrEmpty(SelectedPrinter))
            {
                MessageBox.Show(
                    Services.LocalizationService.Instance.GetString("PleaseSelectPrinter"), 
                    Services.LocalizationService.Instance.GetString("Error"), 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Warning);
                return;
            }

            if (IngestedFiles.Count == 0)
            {
                MessageBox.Show(
                    Services.LocalizationService.Instance.GetString("PleaseAddFilesToPrint"), 
                    Services.LocalizationService.Instance.GetString("Error"), 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Warning);
                return;
            }

            IsPrinting = true;
            ProgressValue = 0;

            try
            {
                await _batchPrintService.PrintBatchAsync(SelectedPrinter, FilesToPrint.ToList());
                MessageBox.Show(
                    Services.LocalizationService.Instance.GetString("BatchPrintingCompletedSuccessfully"), 
                    Services.LocalizationService.Instance.GetString("Success"), 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                var message = Services.LocalizationService.Instance.GetString("PrintingFailedError") + ": " + ex.Message;
                MessageBox.Show(message, Services.LocalizationService.Instance.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsPrinting = false;
                StatusMessage = "Ready";
                ProgressValue = 0;
            }
        }

        [RelayCommand]
        private async Task StopPrinting()
        {
            await _batchPrintService.StopAsync();
            StatusMessage = "Stopping...";
        }
    }

    /// <summary>
    /// Represents a file item in the upload list
    /// </summary>
    public partial class IngestedFileItem : ObservableObject
    {
        [ObservableProperty] private int _index;
        [ObservableProperty] private string _originalName = "";
        [ObservableProperty] private string _fullPath = "";
        [ObservableProperty] private string _folderPath = "";
        [ObservableProperty] private long _sizeBytes;
        [ObservableProperty] private string _sizeDisplay = "";
        [ObservableProperty] private string _fileType = "";
        [ObservableProperty] private DateTime _modifiedDate;
        [ObservableProperty] private int _copies = 1;
        [ObservableProperty] private string _pageRange = "All";
        [ObservableProperty] private bool _isArchiveMember;
        [ObservableProperty] private int _estimatedPages;
    }
}

