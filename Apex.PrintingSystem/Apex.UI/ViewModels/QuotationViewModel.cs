using Apex.Core.Interfaces;
using Apex.Core.Models;
using Apex.Services;
using Apex.Services.Quotation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;

namespace Apex.UI.ViewModels
{
    public partial class QuotationViewModel : ViewModelBase
    {
        private readonly EnhancedQuotationService _quotationService;
        private readonly ISettingsService?         _settingsService;
        private bool _isCalculating = false;

        #region Input Properties

        [ObservableProperty]
        private int _totalPages = 10;

        [ObservableProperty]
        private int _quantity = 1;

        [ObservableProperty]
        private bool _isDoubleSided = false;

        [ObservableProperty]
        private bool _isColor = false;

        [ObservableProperty]
        private string _customerName = "";

        [ObservableProperty]
        private string _jobDescription = "";

        [ObservableProperty]
        private PaperType _selectedPaper;

        [ObservableProperty]
        private decimal _inkCostPerPage = 0.05m;

        [ObservableProperty]
        private decimal _profitMarginPercent = 0;

        [ObservableProperty]
        private decimal _setupCost = 0;

        [ObservableProperty]
        private bool _hasCover = false;

        [ObservableProperty]
        private decimal _coverCost = 5.00m;

        [ObservableProperty]
        private bool _hasLamination = false;

        [ObservableProperty]
        private decimal _laminationCost = 2.00m;

        [ObservableProperty]
        private decimal _manualDiscountPercent = 0;

        #endregion

        #region Output Properties

        [ObservableProperty]
        private decimal _pricePerCopy;

        [ObservableProperty]
        private decimal _pricePerCopyAfterDiscount;

        [ObservableProperty]
        private decimal _totalPrice;

        [ObservableProperty]
        private decimal _appliedDiscountPercent;

        [ObservableProperty]
        private decimal _discountAmount;

        [ObservableProperty]
        private int _sheetsPerCopy;

        [ObservableProperty]
        private decimal _paperCostPerCopy;

        [ObservableProperty]
        private decimal _inkCostPerCopy;

        [ObservableProperty]
        private decimal _coverCostPerCopy;

        [ObservableProperty]
        private decimal _finishingCostPerCopy;

        [ObservableProperty]
        private decimal _profitPerCopy;

        [ObservableProperty]
        private string _breakdownText = "";

        #endregion

        #region Collections

        public ObservableCollection<PaperType> AvailablePaperTypes { get; } = new();
        public ObservableCollection<FinishingService> AvailableFinishingServices { get; } = new();
        public ObservableCollection<FinishingService> SelectedFinishingServices { get; } = new();
        public ObservableCollection<QuantityPricePoint> PriceAnalysis { get; } = new();
        public ObservableCollection<VolumeDiscount> VolumeDiscounts { get; } = new();
        public ObservableCollection<QuantityPricePoint> ComparisonPoints { get; } = new();
        public ObservableCollection<SavedQuoteEntry> SavedQuotes { get; } = new();

        public bool HasSavedQuotes => SavedQuotes.Count > 0;

        #endregion

        public QuotationViewModel(ISettingsService? settingsService = null)
        {
            _settingsService = settingsService;
            try
            {
                _quotationService = new EnhancedQuotationService();

                var papers = PaperTypesCatalog.GetCommonPaperTypes();
                if (papers != null)
                    foreach (var paper in papers)
                        AvailablePaperTypes.Add(paper);

                _selectedPaper = AvailablePaperTypes.FirstOrDefault() ?? new PaperType("A4 Standard", 0.10m);

                var services = FinishingServicesCatalog.GetCommonServices();
                if (services != null)
                    foreach (var service in services)
                        AvailableFinishingServices.Add(service);

                var discounts = DefaultVolumeDiscounts.GetStandardTiers();
                if (discounts != null)
                    foreach (var discount in discounts)
                        VolumeDiscounts.Add(discount);

                SavedQuotes.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSavedQuotes));

                CalculateQuote();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"QuotationViewModel constructor error: {ex}");
                _quotationService = new EnhancedQuotationService();
                _selectedPaper = new PaperType("A4 Standard", 0.10m);
            }
        }

        #region Property Changed Handlers

        partial void OnTotalPagesChanged(int value) => CalculateQuote();
        partial void OnQuantityChanged(int value) => CalculateQuote();
        partial void OnIsDoubleSidedChanged(bool value) => CalculateQuote();
        partial void OnIsColorChanged(bool value)
        {
            InkCostPerPage = value ? 0.25m : 0.05m;
            CalculateQuote();
        }
        partial void OnSelectedPaperChanged(PaperType value) => CalculateQuote();
        partial void OnInkCostPerPageChanged(decimal value) => CalculateQuote();
        partial void OnProfitMarginPercentChanged(decimal value) => CalculateQuote();
        partial void OnSetupCostChanged(decimal value) => CalculateQuote();
        partial void OnHasCoverChanged(bool value) => CalculateQuote();
        partial void OnCoverCostChanged(decimal value) => CalculateQuote();
        partial void OnHasLaminationChanged(bool value) => CalculateQuote();
        partial void OnLaminationCostChanged(decimal value) => CalculateQuote();
        partial void OnManualDiscountPercentChanged(decimal value) => CalculateQuote();

        #endregion

        #region Commands

        [RelayCommand]
        private void CalculateQuote()
        {
            if (_isCalculating) return;

            try
            {
                _isCalculating = true;

                if (_quotationService == null) return;

                var pages = TotalPages <= 0 ? 1 : TotalPages;
                var qty = Quantity <= 0 ? 1 : Quantity;

                CoverOptions? cover = null;
                if (HasCover)
                {
                    cover = new CoverOptions
                    {
                        CoverType = "Cardboard Cover",
                        CoverMaterialCost = CoverCost,
                        HasLamination = HasLamination,
                        LaminationCost = HasLamination ? LaminationCost : 0
                    };
                }

                var quote = _quotationService.CreateFullQuote(
                    pages: pages,
                    quantity: qty,
                    isDoubleSided: IsDoubleSided,
                    isColor: IsColor,
                    paper: SelectedPaper ?? new PaperType("A4 Standard", 0.10m),
                    inkCostPerPage: InkCostPerPage,
                    cover: cover,
                    finishingServices: SelectedFinishingServices?.ToList() ?? new System.Collections.Generic.List<FinishingService>(),
                    profitMarginPercent: ProfitMarginPercent,
                    volumeDiscounts: VolumeDiscounts?.ToList() ?? new System.Collections.Generic.List<VolumeDiscount>(),
                    setupCost: SetupCost,
                    customerName: CustomerName ?? "",
                    jobDescription: JobDescription ?? ""
                );

                if (quote == null) return;

                SheetsPerCopy = quote.SheetsPerCopy;

                if (quote.PerCopyCost != null)
                {
                    PaperCostPerCopy = quote.PerCopyCost.PaperCost;
                    InkCostPerCopy = quote.PerCopyCost.InkCost;
                    CoverCostPerCopy = quote.PerCopyCost.CoverCost;
                    FinishingCostPerCopy = quote.PerCopyCost.FinishingCost;
                    ProfitPerCopy = quote.PerCopyCost.ProfitAmount;
                    PricePerCopy = quote.PerCopyCost.TotalPrice;
                    PricePerCopyAfterDiscount = quote.PerCopyCost.PriceAfterDiscount;
                }

                AppliedDiscountPercent = quote.AppliedDiscountPercent;
                DiscountAmount = quote.DiscountAmount;

                if (quote.TotalCost != null)
                    TotalPrice = quote.TotalCost.FinalTotal;

                // Apply manual discount override
                if (ManualDiscountPercent > 0 && quote.TotalCost != null)
                {
                    var basePrice = quote.TotalCost.TotalBeforeDiscount;
                    DiscountAmount = basePrice * ManualDiscountPercent / 100;
                    TotalPrice = basePrice - DiscountAmount;
                    AppliedDiscountPercent = ManualDiscountPercent;
                    PricePerCopyAfterDiscount = qty > 0 ? TotalPrice / qty : 0;
                }

                BreakdownText = quote.GetDetailedBreakdown() ?? "";

                if (Application.Current != null)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        // Update PriceAnalysis
                        if (quote.PriceAnalysis != null)
                        {
                            PriceAnalysis.Clear();
                            foreach (var point in quote.PriceAnalysis)
                                if (point != null)
                                    PriceAnalysis.Add(point);
                        }

                        // Update comparison table (50, 100, 200, 500)
                        ComparisonPoints.Clear();
                        foreach (var cqty in new[] { 50, 100, 200, 500 })
                        {
                            var temp = _quotationService.CreateFullQuote(
                                pages: pages, quantity: cqty,
                                isDoubleSided: IsDoubleSided, isColor: IsColor,
                                paper: SelectedPaper ?? new PaperType("A4 Standard", 0.10m),
                                inkCostPerPage: InkCostPerPage, cover: cover,
                                finishingServices: SelectedFinishingServices?.ToList() ?? new System.Collections.Generic.List<FinishingService>(),
                                profitMarginPercent: ProfitMarginPercent,
                                volumeDiscounts: VolumeDiscounts?.ToList() ?? new System.Collections.Generic.List<VolumeDiscount>(),
                                setupCost: SetupCost, customerName: "", jobDescription: "");

                            if (temp == null) continue;

                            decimal finalTotal = temp.TotalCost?.FinalTotal ?? 0;
                            decimal pricePerCopy = temp.PerCopyCost?.PriceAfterDiscount ?? 0;
                            decimal discount = temp.AppliedDiscountPercent;

                            if (ManualDiscountPercent > 0 && temp.TotalCost != null)
                            {
                                finalTotal = temp.TotalCost.TotalBeforeDiscount * (1 - ManualDiscountPercent / 100);
                                pricePerCopy = cqty > 0 ? finalTotal / cqty : 0;
                                discount = ManualDiscountPercent;
                            }

                            ComparisonPoints.Add(new QuantityPricePoint
                            {
                                Quantity = cqty,
                                PricePerCopy = pricePerCopy,
                                TotalPrice = finalTotal,
                                AppliedDiscount = discount
                            });
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CalculateQuote error: {ex}");
                BreakdownText = $"Error calculating quote: {ex.Message}";
            }
            finally
            {
                _isCalculating = false;
            }
        }

        [RelayCommand]
        private void SaveQuote()
        {
            SavedQuotes.Insert(0, new SavedQuoteEntry
            {
                CustomerName = string.IsNullOrWhiteSpace(CustomerName) ? "—" : CustomerName,
                JobDescription = JobDescription,
                SavedDate = DateTime.Now,
                TotalPrice = TotalPrice,
                PricePerCopy = PricePerCopyAfterDiscount,
                Quantity = Quantity,
                TotalPages = TotalPages,
                BreakdownText = BreakdownText
            });
        }

        [RelayCommand]
        private void ShareViaWhatsApp()
        {
            var customer = string.IsNullOrWhiteSpace(CustomerName) ? "" : $"*{CustomerName}*\n";
            var msg = $"🖨️ *عرض سعر طباعة*\n" +
                      $"{customer}" +
                      $"📄 {TotalPages} صفحة × {Quantity} نسخة\n" +
                      $"💰 سعر النسخة: *{PricePerCopyAfterDiscount:F2} EGP*\n" +
                      $"📦 الإجمالي: *{TotalPrice:F2} EGP*\n" +
                      (AppliedDiscountPercent > 0 ? $"🏷️ خصم {AppliedDiscountPercent:F0}% مطبق\n" : "") +
                      $"\n_Generated by Apex Print OS_";
            try
            {
                Clipboard.SetText(msg);
                MessageBox.Show("تم نسخ رسالة الواتساب — الصقها في المحادثة", "مشاركة", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { }
        }

        [RelayCommand]
        private void AddFinishingService(FinishingService? service)
        {
            if (service != null && !SelectedFinishingServices.Contains(service))
            {
                SelectedFinishingServices.Add(service);
                CalculateQuote();
            }
        }

        [RelayCommand]
        private void RemoveFinishingService(FinishingService? service)
        {
            if (service != null)
            {
                SelectedFinishingServices.Remove(service);
                CalculateQuote();
            }
        }

        [RelayCommand]
        private void ClearFinishingServices()
        {
            SelectedFinishingServices.Clear();
            CalculateQuote();
        }

        [RelayCommand]
        private void CopyToClipboard()
        {
            try
            {
                Clipboard.SetText(BreakdownText);
                MessageBox.Show("تم نسخ عرض السعر إلى الحافظة", "نسخ", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { }
        }

        [RelayCommand]
        private void ExportToHtml()
        {
            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "HTML Files (*.html)|*.html",
                    DefaultExt = ".html",
                    FileName = $"Quote_{DateTime.Now:yyyyMMdd_HHmmss}"
                };

                if (dialog.ShowDialog() == true)
                {
                    var quote        = CreateCurrentQuote();
                    var companyName  = _settingsService?.GetValueAsync("CompanyName", "أبكس لحلول الطباعة المتكاملة").GetAwaiter().GetResult() ?? "أبكس لحلول الطباعة المتكاملة";
                    var companyPhone = _settingsService?.GetValueAsync("CompanyPhone", "").GetAwaiter().GetResult() ?? "";
                    var companyAddr  = _settingsService?.GetValueAsync("CompanyAddress", "").GetAwaiter().GetResult() ?? "";
                    var html = _quotationService.ExportToHtml(quote, companyName, companyPhone, companyAddr);
                    System.IO.File.WriteAllText(dialog.FileName, html, System.Text.Encoding.UTF8);
                    MessageBox.Show("تم حفظ عرض السعر بنجاح", "تصدير", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطأ في التصدير: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void ExportToPdf()
        {
            try
            {
                var companyName  = _settingsService?.GetValueAsync("CompanyName",  "أبكس لحلول الطباعة المتكاملة").GetAwaiter().GetResult() ?? "أبكس لحلول الطباعة المتكاملة";
                var companyPhone = _settingsService?.GetValueAsync("CompanyPhone", "01099088053").GetAwaiter().GetResult() ?? "01099088053";

                var data = new QuotationData
                {
                    CompanyName      = companyName,
                    CompanyPhone     = companyPhone,
                    QuotationNumber  = $"QT-{DateTime.Now:yyyyMMdd-HHmm}",
                    QuotationDate    = DateTime.Now,
                    ValidUntil       = DateTime.Now.AddDays(7),

                    CustomerName     = CustomerName,
                    JobDescription   = string.IsNullOrWhiteSpace(JobDescription) ? "طباعة وثائق" : JobDescription,
                    TotalPages       = TotalPages,
                    Quantity         = Quantity,
                    IsDoubleSided    = IsDoubleSided,
                    IsColor          = IsColor,
                    PaperSize        = SelectedPaper?.Name ?? "A4",
                    HasCover         = HasCover,
                    HasLamination    = HasLamination,

                    InkCostPerPage   = InkCostPerPage,
                    PaperCostPerPage = SelectedPaper?.PricePerSheet ?? 0.02m,
                    SetupCost        = SetupCost,
                    CoverCost        = HasCover ? CoverCost : 0m,
                    LaminationCost   = HasLamination ? LaminationCost : 0m,
                    DiscountPercent  = AppliedDiscountPercent,

                    Notes = BreakdownText.Length > 0 ? null : null   // Reserved for future notes
                };

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter      = "PDF Files (*.pdf)|*.pdf",
                    DefaultExt  = ".pdf",
                    FileName    = $"عرض_سعر_{CustomerName.Replace(" ", "_")}_{DateTime.Now:yyyyMMdd}"
                };

                if (dialog.ShowDialog() == true)
                {
                    var pdfBytes = QuotationPdfGenerator.Generate(data);
                    File.WriteAllBytes(dialog.FileName, pdfBytes);

                    var result = MessageBox.Show(
                        "تم حفظ عرض السعر كـ PDF بنجاح.\nهل تريد فتح الملف الآن؟",
                        "تصدير PDF",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (result == MessageBoxResult.Yes)
                        Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطأ في إنشاء PDF: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void Print()
        {
            try
            {
                var printDialog = new System.Windows.Controls.PrintDialog();
                if (printDialog.ShowDialog() == true)
                {
                    var document = new System.Windows.Documents.FlowDocument();
                    document.Blocks.Add(new System.Windows.Documents.Paragraph(
                        new System.Windows.Documents.Run(BreakdownText)
                        {
                            FontFamily = new System.Windows.Media.FontFamily("Consolas")
                        }));

                    var paginator = ((System.Windows.Documents.IDocumentPaginatorSource)document).DocumentPaginator;
                    printDialog.PrintDocument(paginator, "Quotation");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطأ في الطباعة: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void ResetToDefaults()
        {
            TotalPages = 10;
            Quantity = 1;
            IsDoubleSided = false;
            IsColor = false;
            CustomerName = "";
            JobDescription = "";
            SelectedPaper = AvailablePaperTypes.FirstOrDefault() ?? new PaperType();
            InkCostPerPage = 0.05m;
            ProfitMarginPercent = 0;
            SetupCost = 0;
            HasCover = false;
            CoverCost = 5.00m;
            HasLamination = false;
            LaminationCost = 2.00m;
            ManualDiscountPercent = 0;
            SelectedFinishingServices.Clear();
            CalculateQuote();
        }

        #endregion

        #region Helper Methods

        private EnhancedQuotation CreateCurrentQuote()
        {
            CoverOptions? cover = null;
            if (HasCover)
            {
                cover = new CoverOptions
                {
                    CoverType = "Cardboard Cover",
                    CoverMaterialCost = CoverCost,
                    HasLamination = HasLamination,
                    LaminationCost = HasLamination ? LaminationCost : 0
                };
            }

            return _quotationService.CreateFullQuote(
                pages: TotalPages,
                quantity: Quantity,
                isDoubleSided: IsDoubleSided,
                isColor: IsColor,
                paper: SelectedPaper ?? new PaperType(),
                inkCostPerPage: InkCostPerPage,
                cover: cover,
                finishingServices: SelectedFinishingServices.ToList(),
                profitMarginPercent: ProfitMarginPercent,
                volumeDiscounts: VolumeDiscounts.ToList(),
                setupCost: SetupCost,
                customerName: CustomerName,
                jobDescription: JobDescription
            );
        }

        #endregion
    }

    public class SavedQuoteEntry
    {
        public string CustomerName { get; set; } = "";
        public string JobDescription { get; set; } = "";
        public DateTime SavedDate { get; set; }
        public decimal TotalPrice { get; set; }
        public decimal PricePerCopy { get; set; }
        public int Quantity { get; set; }
        public int TotalPages { get; set; }
        public string BreakdownText { get; set; } = "";
    }
}
