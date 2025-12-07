using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Win32;
using Apex.Core.Interfaces;
using System;

namespace Apex.UI.ViewModels
{
    public partial class NumberedBooksViewModel : ViewModelBase
    {
        private readonly IPrinterDiscoveryService _printerService;

        [ObservableProperty]
        private string? _selectedTemplate;

        [ObservableProperty]
        private int _startNumber = 1;

        [ObservableProperty]
        private int _endNumber = 100;

        [ObservableProperty]
        private string _prefix = "No. ";

        [ObservableProperty]
        private string _statusMessage = "Ready";

        [ObservableProperty]
        private bool _isGenerating;

        public NumberedBooksViewModel(IPrinterDiscoveryService printerService)
        {
            _printerService = printerService;
        }

        [RelayCommand]
        private void SelectTemplate()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "PDF Files|*.pdf"
            };

            if (dialog.ShowDialog() == true)
            {
                SelectedTemplate = dialog.FileName;
            }
        }

        [RelayCommand]
        private async Task GenerateBook()
        {
            if (string.IsNullOrEmpty(SelectedTemplate))
            {
                MessageBox.Show(
                    Services.LocalizationService.Instance.GetString("PleaseSelectTemplatePDF"), 
                    Services.LocalizationService.Instance.GetString("Error"), 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Warning);
                return;
            }

            IsGenerating = true;
            StatusMessage = "Generating Book...";

            try
            {
                // Placeholder for NumberedBooksEngine integration
                // var engine = new NumberedBooksEngine();
                // await engine.GenerateAsync(SelectedTemplate, StartNumber, EndNumber, Prefix);
                
                await Task.Delay(2000); // Simulation
                
                StatusMessage = "Book Generated Successfully!";
                MessageBox.Show(
                    Services.LocalizationService.Instance.GetString("BookGeneratedSuccessfully"), 
                    Services.LocalizationService.Instance.GetString("Success"), 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusMessage = "Generation Failed";
                var message = Services.LocalizationService.Instance.GetString("GenerationFailed") + ": " + ex.Message;
                MessageBox.Show(message, Services.LocalizationService.Instance.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsGenerating = false;
                StatusMessage = "Ready";
            }
        }
    }
}
