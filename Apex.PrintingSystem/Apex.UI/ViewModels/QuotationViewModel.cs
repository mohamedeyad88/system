using Apex.Core.Models;
using Apex.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace Apex.UI.ViewModels
{
    public partial class QuotationViewModel : ViewModelBase
    {
        private readonly QuotationService _quotationService;
        
        [ObservableProperty]
        private int _totalPages = 1;
        
        [ObservableProperty]
        private bool _isDoubleSided = true;
        
        [ObservableProperty]
        private int _quantity = 1;
        
        [ObservableProperty]
        private decimal _pricePerSheet = 1.25m;
        
        [ObservableProperty]
        private bool _hasCover = false;
        
        [ObservableProperty]
        private decimal _coverPrice = 3.00m;
        
        [ObservableProperty]
        private decimal _profitMargin = 20.0m;
        
        [ObservableProperty]
        private int _sheetsRequired;
        
        [ObservableProperty]
        private decimal _printingCost;
        
        [ObservableProperty]
        private decimal _coverCost;
        
        [ObservableProperty]
        private decimal _finishingCost;
        
        [ObservableProperty]
        private decimal _subTotal;
        
        [ObservableProperty]
        private decimal _profitAmount;
        
        [ObservableProperty]
        private decimal _finalPrice;
        
        [ObservableProperty]
        private string _breakdown = string.Empty;
        
        public ObservableCollection<FinishingServiceViewModel> AvailableServices { get; } = new();
        
        public QuotationViewModel(QuotationService quotationService)
        {
            _quotationService = quotationService;
            LoadAvailableServices();
            CalculateCommand.Execute(null);
        }
        
        private void LoadAvailableServices()
        {
            var services = FinishingServicesCatalog.GetCommonServices();
            foreach (var service in services)
            {
                AvailableServices.Add(new FinishingServiceViewModel(service));
            }
        }
        
        [RelayCommand]
        private void Calculate()
        {
            var selectedServices = AvailableServices
                .Where(s => s.IsSelected)
                .Select(s => s.Service)
                .ToList();
            
            var quote = _quotationService.CreateFullQuote(
                TotalPages,
                IsDoubleSided,
                Quantity,
                PricePerSheet,
                HasCover,
                CoverPrice,
                selectedServices,
                ProfitMargin
            );
            
            // Update UI properties
            SheetsRequired = quote.SheetsRequired;
            PrintingCost = quote.PrintingCost;
            CoverCost = quote.CoverCost;
            FinishingCost = quote.FinishingCost;
            SubTotal = quote.SubTotal;
            ProfitAmount = quote.ProfitAmount;
            FinalPrice = quote.FinalPrice;
            Breakdown = quote.GetBreakdown();
        }
        
        [RelayCommand]
        private void ExportQuote()
        {
            var selectedServices = AvailableServices
                .Where(s => s.IsSelected)
                .Select(s => s.Service)
                .ToList();
            
            var quote = _quotationService.CreateFullQuote(
                TotalPages, IsDoubleSided, Quantity, PricePerSheet,
                HasCover, CoverPrice, selectedServices, ProfitMargin
            );
            
            var export = _quotationService.ExportQuotation(quote);
            
            // Copy to clipboard
            Clipboard.SetText(export);
            MessageBox.Show(
                Services.LocalizationService.Instance.GetString("QuotationCopiedToClipboard"), 
                Services.LocalizationService.Instance.GetString("Success"), 
                MessageBoxButton.OK, 
                MessageBoxImage.Information);
        }
        
        [RelayCommand]
        private void Reset()
        {
            TotalPages = 1;
            IsDoubleSided = true;
            Quantity = 1;
            PricePerSheet = 1.25m;
            HasCover = false;
            CoverPrice = 3.00m;
            ProfitMargin = 20.0m;
            
            foreach (var service in AvailableServices)
            {
                service.IsSelected = false;
            }
            
            CalculateCommand.Execute(null);
        }
        
        // Auto-calculate when properties change
        partial void OnTotalPagesChanged(int value) => CalculateCommand.Execute(null);
        partial void OnIsDoubleSidedChanged(bool value) => CalculateCommand.Execute(null);
        partial void OnQuantityChanged(int value) => CalculateCommand.Execute(null);
        partial void OnPricePerSheetChanged(decimal value) => CalculateCommand.Execute(null);
        partial void OnHasCoverChanged(bool value) => CalculateCommand.Execute(null);
        partial void OnCoverPriceChanged(decimal value) => CalculateCommand.Execute(null);
        partial void OnProfitMarginChanged(decimal value) => CalculateCommand.Execute(null);
    }
    
    public partial class FinishingServiceViewModel : ObservableObject
    {
        public FinishingService Service { get; }
        
        [ObservableProperty]
        private bool _isSelected;
        
        [ObservableProperty]
        private string _name;
        
        [ObservableProperty]
        private decimal _price;
        
        [ObservableProperty]
        private string _description;
        
        public FinishingServiceViewModel(FinishingService service)
        {
            Service = service;
            _name = service.Name;
            _price = service.PricePerPiece;
            _description = service.Description;
        }
        
        partial void OnIsSelectedChanged(bool value)
        {
            // Trigger recalculation when selection changes
        }
    }
}
