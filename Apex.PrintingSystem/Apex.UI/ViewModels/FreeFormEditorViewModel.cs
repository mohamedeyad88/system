using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Apex.Services.Numbering;
using Apex.NumberedBooksEngine.Models;
using System.Collections.ObjectModel;
using System.IO;
using SkiaSharp;
using System.Linq;
using System;
using System.Windows;
using Microsoft.Win32;

namespace Apex.UI.ViewModels
{
    public partial class FreeFormEditorViewModel : ViewModelBase
    {
        private readonly NumberingService _numberingService;
        private int _slotCounter = 0;
        
        [ObservableProperty] private string? _templatePath;
        [ObservableProperty] private ObservableCollection<SlotSpec> _slots = new();
        [ObservableProperty] private SlotSpec? _selectedSlot;
        [ObservableProperty] private SKImage? _previewImage;
        [ObservableProperty] private string? _templateFileName;
        [ObservableProperty] private bool _isPdfTemplate;
        
        // Editor State
        [ObservableProperty] private double _canvasWidth = 800;
        [ObservableProperty] private double _canvasHeight = 1131; // A4 ratio approx
        
        // Slot count for UI binding
        public int SlotCount => Slots.Count;
        
        public FreeFormEditorViewModel(NumberingService numberingService)
        {
            _numberingService = numberingService;
            Slots.CollectionChanged += (s, e) => OnPropertyChanged(nameof(SlotCount));
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
                TemplatePath = dialog.FileName;
                TemplateFileName = Path.GetFileName(dialog.FileName);
                IsPdfTemplate = Path.GetExtension(dialog.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
                
                // For PDF files, we'd need to render the first page to an image
                // For now, just show a placeholder or use the path
                if (!IsPdfTemplate)
                {
                    // Load image dimensions
                    try
                    {
                        using var img = System.Drawing.Image.FromFile(TemplatePath);
                        CanvasWidth = img.Width;
                        CanvasHeight = img.Height;
                    }
                    catch { /* Use defaults */ }
                }
                
                var message = Services.LocalizationService.Instance.GetString("TemplateLoaded") + ": " + TemplateFileName;
                MessageBox.Show(message, Services.LocalizationService.Instance.GetString("Success"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        [RelayCommand]
        private void AddSlot()
        {
            _slotCounter++;
            
            // Position each new slot slightly offset from the previous
            float yOffset = 0.1f + ((_slotCounter - 1) * 0.08f) % 0.6f;
            
            var newSlot = new SlotSpec(
                Id: $"Slot {_slotCounter}",
                X: 0.1f, 
                Y: yOffset, 
                Width: 0.2f, 
                Height: 0.05f,
                FontFamily: "Arial",
                FontSize: 24,
                FontColorHex: "#000000",
                Align: TextAlign.Center,
                Rotation: 0,
                CopyStyles: new[] { new CopyStyle("Original", "#000000", 1.0f) }
            );
            
            Slots.Add(newSlot);
            SelectedSlot = newSlot;
            
            // Force UI update
            OnPropertyChanged(nameof(Slots));
            OnPropertyChanged(nameof(SlotCount));
        }

        [RelayCommand]
        private void RemoveSlot(SlotSpec? slot)
        {
            if (slot != null && Slots.Contains(slot))
            {
                Slots.Remove(slot);
                if (SelectedSlot == slot)
                {
                    SelectedSlot = Slots.FirstOrDefault();
                }
                OnPropertyChanged(nameof(SlotCount));
            }
        }

        [RelayCommand]
        private void SelectSlot(SlotSpec? slot)
        {
            if (slot != null)
            {
                SelectedSlot = slot;
            }
        }

        private void UpdatePreview()
        {
            if (string.IsNullOrEmpty(TemplatePath)) return;

            try
            {
                using var fs = File.OpenRead(TemplatePath);
                PreviewImage = _numberingService.GeneratePreview(fs, Slots.ToList(), 1001);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Preview error: {ex.Message}");
            }
        }
    }
}

