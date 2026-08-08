using Apex.NumberedBooksEngine.Core;
using Apex.NumberedBooksEngine.Models;
using Apex.Services.Numbering;
using Microsoft.Win32;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Apex.NumberedBooksEngine.UI
{
    public partial class NumberedBooksWizard : UserControl, INotifyPropertyChanged
    {
        private Point _startPoint;
        private Rectangle? _currentRect;
        private readonly NumberingService _numberingService;
        private Stream? _templateStream;
        private string? _templatePath;
        private double _previewWidth;
        private double _previewHeight;

        private SlotViewModel? _selectedSlot;
        public SlotViewModel? SelectedSlot
        {
            get => _selectedSlot;
            set { _selectedSlot = value; OnPropertyChanged(); }
        }

        public ObservableCollection<SlotViewModel> Slots { get; } = new();

        public NumberedBooksWizard()
        {
            InitializeComponent();
            _numberingService = new NumberingService();
            DataContext = this;

            // Populate printer list
            foreach (string printer in System.Drawing.Printing.PrinterSettings.InstalledPrinters)
            {
                PrinterCombo.Items.Add(printer);
            }
            if (PrinterCombo.Items.Count > 0)
                PrinterCombo.SelectedIndex = 0;
        }

        private void LoadTemplate_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "PDF or Images|*.pdf;*.png;*.jpg;*.jpeg;*.bmp|All Files|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    _templatePath = openFileDialog.FileName;
                    var preview = TemplateRenderer.RenderPreview(_templatePath);

                    _previewWidth = preview.PixelWidth;
                    _previewHeight = preview.PixelHeight;

                    // Read file into memory for the service if needed, or just keep path
                    var fileBytes = File.ReadAllBytes(_templatePath);
                    _templateStream = new MemoryStream(fileBytes);

                    var brush = new ImageBrush(preview);
                    brush.Stretch = Stretch.Fill;
                    PreviewCanvas.Background = brush;

                    // Clear existing slots
                    Slots.Clear();
                    PreviewCanvas.Children.Clear();
                    SelectedSlot = null;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error loading template: {ex.Message}");
                }
            }
        }

        private void AddSlot_Click(object sender, RoutedEventArgs e)
        {
            Cursor = Cursors.Cross;
            SelectedSlot = null; // Deselect to allow drawing new slot
        }

        private void GeneratePreview_Click(object sender, RoutedEventArgs e)
        {
            if (_templateStream == null && string.IsNullOrEmpty(_templatePath))
            {
                MessageBox.Show("Please load a template first.");
                return;
            }

            if (!Slots.Any())
            {
                MessageBox.Show("Please add at least one numbering slot.");
                return;
            }

            if (!long.TryParse(StartNumberInput.Text, out var startNumber))
            {
                MessageBox.Show("Invalid Start Number.");
                return;
            }

            try
            {
                if (_templateStream != null) _templateStream.Position = 0;

                // Determine format
                var isPdf = _templatePath?.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) == true;
                var format = isPdf ? TemplateFormat.Pdf : TemplateFormat.Image;

                // Convert ViewModels back to Specs
                var slotSpecs = Slots.Select(s => s.Model).ToList();

                // Get total numbers from input
                if (!long.TryParse(TotalNumbersInput.Text, out var totalNumbers))
                    totalNumbers = 100;

                // Generate 4 preview pages
                var previewCount = (int)Math.Min(totalNumbers, 4);
                var previewPages = _numberingService.GeneratePreviewPages(
                    _templateStream!,
                    slotSpecs,
                    startNumber,
                    previewCount,
                    format);

                // Show in PreviewViewer
                var viewer = new PreviewViewer();
                viewer.SetPages(previewPages);

                var window = new Window
                {
                    Title = "Preview (4 Pages)",
                    Content = viewer,
                    Width = 500,
                    Height = 700,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };
                window.ShowDialog();

                // Dispose pages after window closes
                foreach (var page in previewPages)
                    page.Dispose();
            }
            catch (Exception ex)
            {
                var msg = ex.Message;
                if (msg.Contains("PDF"))
                {
                    if (msg.Contains("password") || msg.Contains("encrypted"))
                        MessageBox.Show("Encrypted PDFs are not supported yet.");
                    else
                        MessageBox.Show("PDF cannot be decoded. Ensure the file is not encrypted.");
                }
                else
                {
                    MessageBox.Show($"Error generating preview: {msg}");
                }
            }
        }

        private async void StartPrintJob_Click(object sender, RoutedEventArgs e)
        {
            // Validate inputs
            if (_templateStream == null && string.IsNullOrEmpty(_templatePath))
            {
                MessageBox.Show("Please load a template first.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!Slots.Any())
            {
                MessageBox.Show("Please add at least one numbering slot.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (PrinterCombo.SelectedItem == null)
            {
                MessageBox.Show("Please select a printer.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!long.TryParse(StartNumberInput.Text, out var startNumber) ||
                !long.TryParse(TotalNumbersInput.Text, out var totalNumbers))
            {
                MessageBox.Show("Invalid Start Number or Total Numbers.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var printerName = PrinterCombo.SelectedItem.ToString()!;
            var isPdf = _templatePath?.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) == true;
            var format = isPdf ? TemplateFormat.Pdf : TemplateFormat.Image;
            var slotSpecs = Slots.Select(s => s.Model).ToList();

            // Build job options for validation
            var options = new BookJobOptions(
                TemplateStream: _templateStream,
                TemplatePath: _templatePath,
                TemplateFormat: format,
                Layout: LayoutSpec.A4,
                Slots: slotSpecs,
                StartNumber: startNumber,
                TotalNumbers: totalNumbers,
                PagesPerBook: 1,
                CopiesPerPage: 1,
                Mode: NumberingMode.Auto,
                LowResourceMode: false,
                DegreeOfParallelism: 1,
                CheckpointEvery: 100,
                OutputMode: "DirectPrint",
                OutputPath: ""
            );

            // Validate
            var errors = JobValidator.Validate(options, printerName);
            if (!JobValidator.IsValid(errors))
            {
                var errorMsg = string.Join("\n", errors.Select(e => e.Message));
                MessageBox.Show($"Validation failed:\n{errorMsg}", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Show warnings if any
            var warnings = errors.Where(e => e.Severity == ValidationSeverity.Warning).ToList();
            if (warnings.Any())
            {
                var warningMsg = string.Join("\n", warnings.Select(w => w.Message));
                var result = MessageBox.Show($"Warnings:\n{warningMsg}\n\nContinue anyway?", "Warnings", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes) return;
            }

            // Start print job
            var printService = new WindowsPrintSpoolerService();
            var cts = new CancellationTokenSource();

            var printSettings = new PrintJobSettings
            {
                PrinterName = printerName,
                Copies = 1,
                Dpi = 300
            };

            // Calculate total pages
            var strategy = NumberingStrategyFactory.Create(options);
            long totalPages = options.TotalNumbers / Math.Max(options.Slots.Count, 1);

            // Open monitor window
            var monitor = new PrintJobMonitor(printService, totalPages);
            monitor.Show();

            try
            {
                await printService.StartJobAsync(printSettings, cts.Token);

                // Reset stream
                if (_templateStream != null) _templateStream.Position = 0;
                using var templateLoader = new TemplateLoader();
                using var templateImage = templateLoader.LoadTemplate(_templateStream!, format);
                var composer = new Composer();

                foreach (var pageNumbers in strategy.GeneratePageNumbers(options))
                {
                    if (cts.Token.IsCancellationRequested) break;

                    using var pageImage = composer.ComposePage(templateImage, pageNumbers, options, 0);
                    await printService.PrintPageAsync(pageImage);
                }

                await printService.EndJobAsync();
                MessageBox.Show("Print job completed successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                MessageBox.Show("Print job was cancelled.", "Cancelled", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Print error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void StartStreamingPrint_Click(object sender, RoutedEventArgs e)
        {
            // Validate inputs
            if (string.IsNullOrEmpty(_templatePath))
            {
                MessageBox.Show("Please load a template first.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!Slots.Any())
            {
                MessageBox.Show("Please add at least one numbering slot.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (PrinterCombo.SelectedItem == null)
            {
                MessageBox.Show("Please select a printer.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!long.TryParse(StartNumberInput.Text, out var startNumber) ||
                !long.TryParse(TotalNumbersInput.Text, out var totalNumbers))
            {
                MessageBox.Show("Invalid Start Number or Total Numbers.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var printerName = PrinterCombo.SelectedItem.ToString()!;
            var slotSpecs = Slots.Select(s => s.Model).ToList();

            // Build streaming job options
            var streamingOptions = new NumberedPrintJobOptions(
                PrinterName: printerName,
                TemplatePath: _templatePath,
                Dpi: 300,
                StartNumber: startNumber,
                TotalNumbers: totalNumbers,
                CopiesPerPage: 1,
                Slots: slotSpecs,
                UsePrinterStoredTemplate: false, // Will detect automatically
                LowResourceMode: true,
                CheckpointEvery: 500
            );

            // Calculate total pages
            long totalPages = totalNumbers / Math.Max(slotSpecs.Count, 1);

            // Create progress reporter
            var progress = new Progress<ProgressInfo>(info =>
            {
                Dispatcher.Invoke(() =>
                {
                    // Update any UI elements with progress
                });
            });

            // Run streaming print job
            var orchestrator = new JobOrchestrator();
            var cts = new CancellationTokenSource();

            // Open monitor
            var printService = new WindowsPrintSpoolerService();
            var monitor = new PrintJobMonitor(printService, totalPages);
            monitor.Show();

            try
            {
                var result = await orchestrator.RunStreamingPrintJobAsync(streamingOptions, progress, cts.Token);

                if (result.Success)
                {
                    MessageBox.Show(
                        $"Streaming print completed!\n\n" +
                        $"Pages printed: {result.TotalPagesGenerated}\n" +
                        $"Template sent once, numbers streamed per page.",
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    var errorMsg = string.Join("\n", result.Errors);
                    MessageBox.Show($"Print failed: {errorMsg}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Streaming print error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Deselect if clicking on empty space (unless drawing)
            if (Cursor != Cursors.Cross && e.OriginalSource == PreviewCanvas)
            {
                SelectedSlot = null;
                RemoveAdorners();
                return;
            }

            if (Cursor != Cursors.Cross) return;

            _startPoint = e.GetPosition(PreviewCanvas);

            _currentRect = new Rectangle
            {
                Stroke = Brushes.Red,
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb(50, 255, 0, 0))
            };

            Canvas.SetLeft(_currentRect, _startPoint.X);
            Canvas.SetTop(_currentRect, _startPoint.Y);
            PreviewCanvas.Children.Add(_currentRect);
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _currentRect != null)
            {
                var pos = e.GetPosition(PreviewCanvas);
                var x = Math.Min(pos.X, _startPoint.X);
                var y = Math.Min(pos.Y, _startPoint.Y);
                var w = Math.Abs(pos.X - _startPoint.X);
                var h = Math.Abs(pos.Y - _startPoint.Y);

                _currentRect.Width = w;
                _currentRect.Height = h;
                Canvas.SetLeft(_currentRect, x);
                Canvas.SetTop(_currentRect, y);
            }
        }

        private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_currentRect != null)
            {
                var left = Canvas.GetLeft(_currentRect);
                var top = Canvas.GetTop(_currentRect);
                var width = _currentRect.Width;
                var height = _currentRect.Height;

                var canvasW = PreviewCanvas.ActualWidth;
                var canvasH = PreviewCanvas.ActualHeight;

                if (canvasW > 0 && canvasH > 0 && width > 5 && height > 5)
                {
                    var slotSpec = new SlotSpec(
                        Id: Guid.NewGuid().ToString(),
                        X: (float)(left / canvasW),
                        Y: (float)(top / canvasH),
                        Width: (float)(width / canvasW),
                        Height: (float)(height / canvasH),
                        FontFamily: "Arial",
                        FontSize: 12,
                        FontColorHex: "#FF0000",
                        Align: TextAlign.Center,
                        Rotation: 0,
                        CopyStyles: Array.Empty<CopyStyle>()
                    );

                    var slotVm = new SlotViewModel(slotSpec);
                    Slots.Add(slotVm);

                    // Create visual representation
                    CreateSlotVisual(slotVm);
                }

                PreviewCanvas.Children.Remove(_currentRect);
                _currentRect = null;
                Cursor = Cursors.Arrow;
            }
        }

        private void CreateSlotVisual(SlotViewModel slotVm)
        {
            var rect = new Rectangle
            {
                Stroke = Brushes.Blue,
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb(50, 0, 0, 255)),
                Width = slotVm.Width * PreviewCanvas.ActualWidth,
                Height = slotVm.Height * PreviewCanvas.ActualHeight,
                Tag = slotVm
            };

            Canvas.SetLeft(rect, slotVm.X * PreviewCanvas.ActualWidth);
            Canvas.SetTop(rect, slotVm.Y * PreviewCanvas.ActualHeight);

            rect.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                SelectSlot(slotVm, rect);
            };

            // Bind visual to VM changes
            slotVm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SlotViewModel.X)) Canvas.SetLeft(rect, slotVm.X * PreviewCanvas.ActualWidth);
                if (e.PropertyName == nameof(SlotViewModel.Y)) Canvas.SetTop(rect, slotVm.Y * PreviewCanvas.ActualHeight);
                if (e.PropertyName == nameof(SlotViewModel.Width)) rect.Width = slotVm.Width * PreviewCanvas.ActualWidth;
                if (e.PropertyName == nameof(SlotViewModel.Height)) rect.Height = slotVm.Height * PreviewCanvas.ActualHeight;
            };

            PreviewCanvas.Children.Add(rect);
            SelectSlot(slotVm, rect);
        }

        private void SelectSlot(SlotViewModel slotVm, Rectangle visual)
        {
            SelectedSlot = slotVm;
            RemoveAdorners();

            var adornerLayer = AdornerLayer.GetAdornerLayer(visual);
            if (adornerLayer != null)
            {
                var adorner = new SlotAdorner(visual);
                adornerLayer.Add(adorner);

                // Update VM when adorner changes visual
                visual.LayoutUpdated += (s, e) =>
                {
                    if (PreviewCanvas.ActualWidth > 0 && PreviewCanvas.ActualHeight > 0)
                    {
                        var left = Canvas.GetLeft(visual);
                        var top = Canvas.GetTop(visual);
                        slotVm.X = (float)(left / PreviewCanvas.ActualWidth);
                        slotVm.Y = (float)(top / PreviewCanvas.ActualHeight);
                        slotVm.Width = (float)(visual.Width / PreviewCanvas.ActualWidth);
                        slotVm.Height = (float)(visual.Height / PreviewCanvas.ActualHeight);
                    }
                };
            }
        }

        private void RemoveAdorners()
        {
            var layer = AdornerLayer.GetAdornerLayer(PreviewCanvas);
            // This is tricky because AdornerLayer is per element or global. 
            // We need to find adorners on the children.
            // Simplified: We just clear adorners from the currently selected visual if we tracked it, 
            // or we iterate children.
            // For now, let's assume we can just clear all adorners on the canvas children if possible,
            // or better, track the current adorner.

            // Since we don't easily track the adorner instance here without extra state, 
            // we'll rely on the fact that we only add one.
            // A robust way is to iterate all children and Remove adorners.
            foreach (UIElement child in PreviewCanvas.Children)
            {
                var al = AdornerLayer.GetAdornerLayer(child);
                if (al != null)
                {
                    var adorners = al.GetAdorners(child);
                    if (adorners != null)
                    {
                        foreach (var ad in adorners)
                            if (ad is SlotAdorner) al.Remove(ad);
                    }
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
