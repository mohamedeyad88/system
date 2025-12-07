using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Apex.Services.Numbering;
using Apex.NumberedBooksEngine.Models;
using Apex.NumberedBooksEngine.Core;
using Apex.Core.Interfaces;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Threading;
using System;
using System.Linq;
using System.IO;
using Microsoft.Win32;
using System.Windows.Media.Imaging;

namespace Apex.UI.ViewModels
{
    public partial class NumberingWizardViewModel : ViewModelBase
    {
        private readonly NumberingService _numberingService;
        private readonly IPrinterDiscoveryService _printerService;
        private readonly INumberSequencer _sequencer;
        private CancellationTokenSource? _cts;
        private int _slotCounter = 0;
        
        // Numbering Settings
        [ObservableProperty] private long _startNumber = 1;
        [ObservableProperty] private long _totalNumbers = 10000;
        
        // Copies
        [ObservableProperty] private bool _useCopy1 = true;
        [ObservableProperty] private bool _useCopy2;
        [ObservableProperty] private bool _useCopy3;
        
        // Mode
        [ObservableProperty] private bool _isAutoDetect = true;
        [ObservableProperty] private bool _isLinearMode;
        [ObservableProperty] private bool _isImposedMode;
        [ObservableProperty] private NumberingMode _mode = NumberingMode.Linear;
        
        // Template & Canvas
        [ObservableProperty] private string? _templatePath;
        [ObservableProperty] private string? _templateFileName;
        [ObservableProperty] private double _canvasWidth = 595;
        [ObservableProperty] private double _canvasHeight = 842;
        [ObservableProperty] private BitmapSource? _templateImage;
        
        // Slots
        [ObservableProperty] private ObservableCollection<NumberSlot> _slots = new();
        [ObservableProperty] private NumberSlot? _selectedSlot;
        
        // Typography (global defaults)
        [ObservableProperty] private string _fontFamily = "Arial";
        [ObservableProperty] private int _fontSize = 24;
        [ObservableProperty] private string _fontColor = "#000000";
        [ObservableProperty] private bool _isBold;
        [ObservableProperty] private int _rotation;
        [ObservableProperty] private int _opacity = 100;
        
        // Printers
        [ObservableProperty] private string _selectedPrinter = "";
        [ObservableProperty] private ObservableCollection<string> _availablePrinters = new();
        
        // Typography - Available Fonts
        [ObservableProperty] private ObservableCollection<string> _availableFonts = new()
        {
            "Arial", "Times New Roman", "Courier New", "Calibri", "Tahoma", 
            "Verdana", "Georgia", "Trebuchet MS", "Impact", "Comic Sans MS"
        };
        
        // Progress
        [ObservableProperty] private bool _isProcessing;
        [ObservableProperty] private double _progress;
        [ObservableProperty] private string _statusMessage = "Ready";
        [ObservableProperty] private long _pagesGenerated;
        [ObservableProperty] private long _totalPages;
        
        // Print Progress
        [ObservableProperty] private bool _isPrinting;
        [ObservableProperty] private bool _isPaused;
        [ObservableProperty] private double _printProgress;
        [ObservableProperty] private string _printStatus = "";
        
        public int CopiesCount => (UseCopy1 ? 1 : 0) + (UseCopy2 ? 1 : 0) + (UseCopy3 ? 1 : 0);

        public NumberingWizardViewModel(
            NumberingService numberingService, 
            FreeFormEditorViewModel editor,
            IPrinterDiscoveryService printerService)
        {
            _numberingService = numberingService;
            _printerService = printerService;
            _sequencer = new NumberSequencer();
        }

        public override async Task InitializeAsync()
        {
            await LoadPrintersAsync();
        }

        private async Task LoadPrintersAsync()
        {
            await Task.Run(async () =>
            {
                var printers = await _printerService.ScanAsync();
                
                Application.Current.Dispatcher.Invoke(() =>
                {
                    AvailablePrinters.Clear();
                    foreach (var p in printers)
                    {
                        AvailablePrinters.Add(p.Name);
                    }
                    if (AvailablePrinters.Any())
                        SelectedPrinter = AvailablePrinters.First();
                });
            });
        }

        [RelayCommand]
        private void LoadTemplate()
        {
            var dialog = new OpenFileDialog 
            { 
                Filter = "All Supported|*.pdf;*.png;*.jpg;*.jpeg|PDF Files|*.pdf|Images|*.png;*.jpg;*.jpeg",
                Title = "Select Template File"
            };
            
            if (dialog.ShowDialog() == true)
            {
                TemplateFileName = Path.GetFileName(dialog.FileName);
                
                try
                {
                    if (dialog.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    {
                        // For PDF, render first page as image
                        LoadPdfTemplate(dialog.FileName);
                    }
                    else
                    {
                        // Load image directly
                        TemplatePath = dialog.FileName;
                        
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(dialog.FileName);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        
                        TemplateImage = bitmap;
                        CanvasWidth = bitmap.PixelWidth;
                        CanvasHeight = bitmap.PixelHeight;
                    }
                    
                    StatusMessage = $"Loaded: {TemplateFileName}";
                }
                catch (Exception ex)
                {
                    var message = Services.LocalizationService.Instance.GetString("ErrorLoadingTemplate") + ": " + ex.Message;
                    MessageBox.Show(message, Services.LocalizationService.Instance.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void LoadPdfTemplate(string pdfPath)
        {
            try
            {
                // Store the PDF path for numbering engine
                TemplatePath = pdfPath;
                
                // Try to render PDF first page using PdfiumViewer
                using (var document = PdfiumViewer.PdfDocument.Load(pdfPath))
                {
                    var page = document.Render(0, 150, 150, true);
                    
                    // Convert to WPF BitmapSource
                    var bitmap = new System.Drawing.Bitmap(page);
                    var bitmapData = bitmap.LockBits(
                        new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                        System.Drawing.Imaging.ImageLockMode.ReadOnly,
                        System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    
                    var bitmapSource = BitmapSource.Create(
                        bitmap.Width, bitmap.Height,
                        96, 96,
                        System.Windows.Media.PixelFormats.Bgra32,
                        null,
                        bitmapData.Scan0,
                        bitmapData.Height * bitmapData.Stride,
                        bitmapData.Stride);
                    
                    bitmap.UnlockBits(bitmapData);
                    bitmapSource.Freeze();
                    
                    TemplateImage = bitmapSource;
                    CanvasWidth = bitmap.Width;
                    CanvasHeight = bitmap.Height;
                    
                    bitmap.Dispose();
                    page.Dispose();
                }
            }
            catch (Exception ex)
            {
                // Fall back: show message but keep the path for numbering
                var message = Services.LocalizationService.Instance.GetString("PDFPreviewNotAvailable") + ": " + ex.Message + "\n" +
                             Services.LocalizationService.Instance.GetString("PDFWillStillBeUsed");
                MessageBox.Show(message, Services.LocalizationService.Instance.GetString("Info"), MessageBoxButton.OK, MessageBoxImage.Information);
                
                // Set default A4 dimensions
                CanvasWidth = 595;
                CanvasHeight = 842;
            }
        }

        [RelayCommand]
        private void AddSlot()
        {
            _slotCounter++;
            
            // Position in a 2x2 grid pattern
            int col = (_slotCounter - 1) % 2;
            int row = ((_slotCounter - 1) / 2) % 2;
            
            float x = 0.1f + (col * 0.45f);
            float y = 0.1f + (row * 0.45f);
            
            // Calculate preview number
            long previewNum = StartNumber + ((_slotCounter - 1) * (TotalNumbers / Math.Max(1, Slots.Count + 1)));
            
            var newSlot = new NumberSlot
            {
                Id = $"Slot {_slotCounter}",
                X = x,
                Y = y,
                Width = 0.15f,
                Height = 0.08f,
                FontFamily = FontFamily,
                FontSize = FontSize,
                FontColor = FontColor,
                IsBold = IsBold,
                Rotation = Rotation,
                PreviewNumber = previewNum.ToString("D4"),
                IsSelected = true
            };
            
            // Deselect all other slots
            foreach (var s in Slots) s.IsSelected = false;
            
            Slots.Add(newSlot);
            SelectedSlot = newSlot;
            UpdateSlotPreviews();
            UpdateTotalPages();
        }

        private void UpdateSlotPreviews()
        {
            if (Slots.Count == 0) return;

            // Convert UI slots to engine SlotSpecs for the sequencer
            var slotSpecs = Slots.Select(s => new SlotSpec(
                Id: s.Id,
                X: s.X,
                Y: s.Y,
                Width: s.Width,
                Height: s.Height,
                FontFamily: s.FontFamily,
                FontSize: s.FontSize,
                FontColorHex: s.FontColor,
                Align: TextAlign.Center,
                Rotation: s.Rotation,
                CopyStyles: null
            )).ToList();

            // Generate first page assignment using the sequencer (for preview)
            var mode = IsLinearMode ? NumberingMode.Linear : NumberingMode.Imposed;
            var assignments = mode == NumberingMode.Linear
                ? _sequencer.GenerateLinearAssignments(StartNumber, TotalNumbers, slotSpecs)
                : _sequencer.GenerateImposedAssignments(StartNumber, TotalNumbers, slotSpecs);

            // Get the first page assignment to show as preview
            var firstPage = assignments.FirstOrDefault();
            if (firstPage != null)
            {
                foreach (var slot in Slots)
                {
                    var assignment = firstPage.SlotNumbers.FirstOrDefault(a => a.SlotId == slot.Id);
                    if (assignment != null)
                    {
                        slot.PreviewNumber = assignment.Number.ToString("D4");
                    }
                }
            }
        }

        [RelayCommand]
        private void DeleteSelectedSlot()
        {
            if (SelectedSlot != null)
            {
                Slots.Remove(SelectedSlot);
                SelectedSlot = Slots.LastOrDefault();
                if (SelectedSlot != null)
                    SelectedSlot.IsSelected = true;
                UpdateSlotPreviews();
                UpdateTotalPages();
            }
        }

        partial void OnSelectedSlotChanged(NumberSlot? value)
        {
            // Update selection visual
            foreach (var s in Slots)
                s.IsSelected = s == value;
        }

        partial void OnTotalNumbersChanged(long value)
        {
            UpdateSlotPreviews();
            UpdateTotalPages();
        }

        partial void OnStartNumberChanged(long value)
        {
            UpdateSlotPreviews();
        }

        private void UpdateTotalPages()
        {
            TotalPages = Slots.Count > 0 
                ? TotalNumbers / Slots.Count * CopiesCount 
                : 0;
        }

        [ObservableProperty] private BitmapSource? _cleanPreviewImage;

        [RelayCommand]
        private async Task GeneratePreview()
        {
            if (Slots.Count == 0)
            {
                MessageBox.Show(
                    Services.LocalizationService.Instance.GetString("PleaseAddAtLeastOneSlot"), 
                    Services.LocalizationService.Instance.GetString("Info"), 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrEmpty(TemplatePath) || !File.Exists(TemplatePath))
            {
                 MessageBox.Show(
                    Services.LocalizationService.Instance.GetString("TemplateFileNotFound"), 
                    Services.LocalizationService.Instance.GetString("Error"), 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Error);
                 return;
            }
            
            IsProcessing = true;
            StatusMessage = "Generating clean preview...";

            try
            {
                await Task.Run(() =>
                {
                    // Convert UI slots to engine SlotSpecs
                    var slotSpecs = Slots.Select(s => new SlotSpec(
                        Id: s.Id,
                        X: s.X,
                        Y: s.Y,
                        Width: s.Width,
                        Height: s.Height,
                        FontFamily: s.FontFamily,
                        FontSize: s.FontSize,
                        FontColorHex: s.FontColor,
                        Align: TextAlign.Center, // Default, can be exposed
                        Rotation: s.Rotation,
                        CopyStyles: null
                    )).ToList();

                    // Generate ONE preview page using the same engine as the printer
                    MemoryStream stream;
                    if (TemplatePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                         stream = new MemoryStream(File.ReadAllBytes(TemplatePath));
                    else
                         stream = new MemoryStream(File.ReadAllBytes(TemplatePath));

                    // Use the format from the file extension
                    var format = TemplatePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) 
                        ? TemplateFormat.Pdf 
                        : TemplateFormat.Image;

                    // Generate clean SKImage
                    using var skImage = _numberingService.GeneratePreview(stream, slotSpecs, StartNumber, format);
                    
                    // Convert to displayable BitmapSource
                    using var data = skImage.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                    using var ms = new MemoryStream();
                    data.SaveTo(ms);
                    ms.Position = 0;
                    
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.StreamSource = ms;
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        CleanPreviewImage = bitmap;
                    });
                });

                StatusMessage = "Preview generated.";
                // In a real app, this would open a dialog showing CleanPreviewImage
                // For now, valid generation proves isolation.
                var message = Services.LocalizationService.Instance.GetString("CleanPreviewGeneratedSuccessfully") + ".\n" +
                             Services.LocalizationService.Instance.GetString("Resolution") + ": " + CleanPreviewImage?.PixelWidth + "x" + CleanPreviewImage?.PixelHeight;
                MessageBox.Show(message, Services.LocalizationService.Instance.GetString("Success"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                 var message = Services.LocalizationService.Instance.GetString("ErrorGeneratingPreview") + ": " + ex.Message;
                 MessageBox.Show(message, Services.LocalizationService.Instance.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsProcessing = false;
            }
        }

        [RelayCommand]
        private async Task StartPrint()
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

            await ExecutePrintJob(false);
        }

        [RelayCommand]
        private async Task StartStreamPrint()
        {
            if (!Slots.Any())
            {
                MessageBox.Show(
                    Services.LocalizationService.Instance.GetString("PleaseAddAtLeastOneNumberSlot"), 
                    Services.LocalizationService.Instance.GetString("Error"), 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(SelectedPrinter) && !AvailablePrinters.Any())
            {
                MessageBox.Show(
                    Services.LocalizationService.Instance.GetString("NoPrintersAvailable"), 
                    Services.LocalizationService.Instance.GetString("Error"), 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Warning);
                return;
            }
            
            if (string.IsNullOrEmpty(SelectedPrinter) && AvailablePrinters.Any())
            {
                SelectedPrinter = AvailablePrinters.First();
            }

            await ExecutePrintJob(true);
        }

        private async Task ExecutePrintJob(bool isStreaming)
        {
            // Pre-flight validation
            var validationErrors = ValidatePrintJob();
            if (validationErrors.Any())
            {
                var errorMessages = validationErrors.Where(e => e.Severity == ValidationSeverity.Error);
                var warningMessages = validationErrors.Where(e => e.Severity == ValidationSeverity.Warning);

                if (errorMessages.Any())
                {
                    var errorText = string.Join("\n", errorMessages.Select(e => $"❌ {e.Message}"));
                    var message = Services.LocalizationService.Instance.GetString("CannotStartPrint") + ":\n\n" + errorText;
                    MessageBox.Show(message, Services.LocalizationService.Instance.GetString("ValidationErrors"), 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (warningMessages.Any())
                {
                    var warningText = string.Join("\n", warningMessages.Select(e => $"⚠ {e.Message}"));
                    var message = Services.LocalizationService.Instance.GetString("WarningsFound") + ":\n\n" + warningText + "\n\n" +
                                 Services.LocalizationService.Instance.GetString("ContinueAnyway");
                    var result = MessageBox.Show(message, Services.LocalizationService.Instance.GetString("Warning"), 
                        MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (result != MessageBoxResult.Yes)
                        return;
                }
            }

            IsProcessing = true;
            IsPrinting = true;
            StatusMessage = isStreaming ? "Starting Stream Print..." : "Starting Print...";
            PrintStatus = "Initializing...";
            Progress = 0;
            PrintProgress = 0;
            PagesGenerated = 0;
            _cts = new CancellationTokenSource();

            UpdateTotalPages();

            var progress = new Progress<ProgressInfo>(info =>
            {
                Progress = info.Percent;
                PrintProgress = info.Percent;
                PagesGenerated = info.PagesGenerated;
                StatusMessage = $"Printing {info.PagesGenerated:N0} / {TotalPages:N0} pages...";
                PrintStatus = IsPaused ? "Paused" : $"Page {info.PagesGenerated:N0} of {TotalPages:N0}";
            });

            try
            {
                var engineSlots = Slots.Select(s => new SlotSpec(
                    Id: s.Id,
                    X: s.X,
                    Y: s.Y,
                    Width: s.Width,
                    Height: s.Height,
                    FontFamily: s.FontFamily,
                    FontSize: s.FontSize,
                    FontColorHex: s.FontColor,
                    Align: TextAlign.Center,
                    Rotation: s.Rotation,
                    CopyStyles: null  // No "Original" label
                )).ToList();

                JobResult result;

                if (isStreaming)
                {
                    // Stream Print: Direct to printer using GDI spooler
                    result = await _numberingService.RunStreamingJobAsync(
                        SelectedPrinter,
                        TemplatePath ?? "",
                        engineSlots,
                        StartNumber,
                        TotalNumbers,
                        CopiesCount,
                        progress,
                        _cts.Token);
                }
                else
                {
                    // Normal Print: Generate PDF to temp file, then print via shell
                    var tempPdfPath = Path.Combine(Path.GetTempPath(), $"ApexNumber_{Guid.NewGuid():N}.pdf");
                    
                    var options = new BookJobOptions(
                        TemplateStream: TemplatePath != null ? File.OpenRead(TemplatePath) : null,
                        TemplatePath: TemplatePath ?? "",
                        TemplateFormat: TemplatePath?.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) == true 
                            ? TemplateFormat.Pdf : TemplateFormat.Image,
                        Layout: LayoutSpec.A4,
                        Slots: engineSlots,
                        StartNumber: StartNumber,
                        TotalNumbers: TotalNumbers,
                        PagesPerBook: 50,
                        CopiesPerPage: CopiesCount,
                        Mode: IsLinearMode ? NumberingMode.Linear : NumberingMode.Imposed,
                        LowResourceMode: false,
                        DegreeOfParallelism: Environment.ProcessorCount,
                        CheckpointEvery: 100,
                        OutputMode: "SinglePdf",
                        OutputPath: tempPdfPath
                    );

                    result = await _numberingService.RunJobAsync(options, progress, _cts.Token);
                    
                    // If PDF generation succeeded, send to printer
                    if (result.Success && File.Exists(tempPdfPath))
                    {
                        StatusMessage = "Sending to printer...";
                        
                        try
                        {
                            // Use shell PrintTo verb to send PDF to printer
                            var printProcess = new System.Diagnostics.Process();
                            printProcess.StartInfo.FileName = tempPdfPath;
                            printProcess.StartInfo.Verb = "printto";
                            printProcess.StartInfo.Arguments = $"\"{SelectedPrinter}\"";
                            printProcess.StartInfo.UseShellExecute = true;
                            printProcess.StartInfo.CreateNoWindow = true;
                            printProcess.Start();
                            
                            // Wait a bit for print job to be submitted
                            await Task.Delay(2000);
                        }
                        finally
                        {
                            // Cleanup temp file after a delay
                            _ = Task.Run(async () =>
                            {
                                await Task.Delay(30000); // Wait 30s before deleting
                                try { File.Delete(tempPdfPath); } catch { }
                            });
                        }
                    }
                }
                
                if (result.Success)
                {
                    Progress = 100;
                    StatusMessage = $"Complete! {result.TotalPagesGenerated:N0} pages printed.";
                    MessageBox.Show(
                        $"Print Completed!\n\nPages: {result.TotalPagesGenerated:N0}\nPrinter: {SelectedPrinter}", 
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    StatusMessage = "Print Failed.";
                    var message = Services.LocalizationService.Instance.GetString("PrintFailedWithErrors") + ":\n" + string.Join("\n", result.Errors);
                    MessageBox.Show(message, Services.LocalizationService.Instance.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Cancelled.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                var message = Services.LocalizationService.Instance.GetString("Error") + ": " + ex.Message;
                MessageBox.Show(message, Services.LocalizationService.Instance.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsProcessing = false;
                IsPrinting = false;
                _cts = null;
            }
        }

        private List<ValidationError> ValidatePrintJob()
        {
            var errors = new List<ValidationError>();

            // Check slots
            if (!Slots.Any())
            {
                errors.Add(new ValidationError
                {
                    Code = "NO_SLOTS",
                    Message = "No numbering slots defined. Please add at least one slot.",
                    Severity = ValidationSeverity.Error
                });
            }

            // Check template
            if (string.IsNullOrEmpty(TemplatePath))
            {
                errors.Add(new ValidationError
                {
                    Code = "NO_TEMPLATE",
                    Message = "No template loaded. Please load a template first.",
                    Severity = ValidationSeverity.Error
                });
            }

            // Check printer
            if (string.IsNullOrEmpty(SelectedPrinter))
            {
                errors.Add(new ValidationError
                {
                    Code = "NO_PRINTER",
                    Message = "No printer selected.",
                    Severity = ValidationSeverity.Error
                });
            }

            // Check total numbers
            if (TotalNumbers <= 0)
            {
                errors.Add(new ValidationError
                {
                    Code = "INVALID_TOTAL",
                    Message = "Total numbers must be greater than 0.",
                    Severity = ValidationSeverity.Error
                });
            }

            // Warnings
            if (TotalNumbers < Slots.Count)
            {
                errors.Add(new ValidationError
                {
                    Code = "TOO_FEW_NUMBERS",
                    Message = "Total numbers is less than slot count. Some slots will be empty.",
                    Severity = ValidationSeverity.Warning
                });
            }

            // Check for slots outside bounds
            foreach (var slot in Slots)
            {
                if (slot.X < 0 || slot.X > 1 || slot.Y < 0 || slot.Y > 1)
                {
                    errors.Add(new ValidationError
                    {
                        Code = "SLOT_OUTSIDE",
                        Message = $"Slot '{slot.Id}' is outside the template area.",
                        Severity = ValidationSeverity.Warning,
                        SlotId = slot.Id
                    });
                }
            }

            return errors;
        }

        [RelayCommand]
        private void Cancel()
        {
            _cts?.Cancel();
            IsPaused = false;
            StatusMessage = "Cancelled";
        }

        [RelayCommand]
        private void PauseResume()
        {
            IsPaused = !IsPaused;
            PrintStatus = IsPaused ? "Paused" : "Printing...";
        }

        [RelayCommand]
        private void ZoomIn() { /* Implement zoom */ }

        [RelayCommand]
        private void ZoomOut() { /* Implement zoom */ }

        [RelayCommand]
        private void FitToScreen() { /* Implement fit */ }
    }

    // NumberSlot model with all properties
    public partial class NumberSlot : ObservableObject
    {
        [ObservableProperty] private string _id = "";
        [ObservableProperty] private float _x;
        [ObservableProperty] private float _y;
        [ObservableProperty] private float _width = 0.15f;
        [ObservableProperty] private float _height = 0.08f;
        [ObservableProperty] private string _fontFamily = "Arial";
        [ObservableProperty] private int _fontSize = 24;
        [ObservableProperty] private string _fontColor = "#000000";
        [ObservableProperty] private bool _isBold;
        [ObservableProperty] private int _rotation;
        [ObservableProperty] private string _alignment = "Center";
        [ObservableProperty] private int _opacity = 100;
        [ObservableProperty] private string _previewNumber = "0001";
        [ObservableProperty] private bool _isSelected;
    }
}
