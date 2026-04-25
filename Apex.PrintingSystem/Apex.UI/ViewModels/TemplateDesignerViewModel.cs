using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Apex.Services.Templates;
using Apex.Services.SmartVariables.Models;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Apex.UI.ViewModels
{
    // ── Canvas slot wrapper ───────────────────────────────────────────────────
    public class CanvasSlotItem
    {
        private const double MmToPt = 2.835;

        public TemplateSlotDefinition Slot { get; init; } = null!;

        public double Left   => Slot.X      * MmToPt;
        public double Top    => Slot.Y      * MmToPt;
        public double Width  => Math.Max(Slot.Width  * MmToPt, 12);
        public double Height => Math.Max(Slot.Height * MmToPt, 12);

        public string Label   => string.IsNullOrWhiteSpace(Slot.Name) ? (Slot.VariableName ?? "?") : Slot.Name;
        public string TypeTag => Slot.DataType switch
        {
            SlotDataType.Text    => "T",
            SlotDataType.Number  => "#",
            SlotDataType.Date    => "D",
            SlotDataType.Image   => "I",
            SlotDataType.Barcode => "B",
            SlotDataType.QrCode  => "Q",
            SlotDataType.Counter => "C",
            _                    => "?"
        };

        public string BorderColor => Slot.DataType switch
        {
            SlotDataType.Text    => "#3B82F6",
            SlotDataType.Number  => "#10B981",
            SlotDataType.Date    => "#F59E0B",
            SlotDataType.Image   => "#8B5CF6",
            SlotDataType.Barcode => "#EF4444",
            SlotDataType.QrCode  => "#EF4444",
            SlotDataType.Counter => "#06B6D4",
            _                    => "#3B82F6"
        };

        public string FillColor => Slot.DataType switch
        {
            SlotDataType.Text    => "#193B82F6",
            SlotDataType.Number  => "#1910B981",
            SlotDataType.Date    => "#19F59E0B",
            SlotDataType.Image   => "#198B5CF6",
            SlotDataType.Barcode => "#19EF4444",
            SlotDataType.QrCode  => "#19EF4444",
            SlotDataType.Counter => "#1906B6D4",
            _                    => "#193B82F6"
        };
    }

    // ── ViewModel ─────────────────────────────────────────────────────────────
    public partial class TemplateDesignerViewModel : ViewModelBase
    {
        private readonly TemplateLibrary _library = TemplateLibrary.Instance;
        private Dictionary<string, byte[]> _currentAssets = new(StringComparer.OrdinalIgnoreCase);

        // Page preset sizes (mm)
        private static readonly Dictionary<string, (double W, double H)> PagePresets = new()
        {
            ["A4"]        = (210,   297),
            ["A5"]        = (148,   210),
            ["Letter"]    = (215.9, 279.4),
            ["بطاقة عمل"] = (85.6,  54),
            ["ملصق"]      = (101.6, 63.5),
        };

        // ── Library ───────────────────────────────────────────────────────────
        [ObservableProperty] private ObservableCollection<TemplateLibraryEntry> _templates   = new();
        [ObservableProperty] private TemplateLibraryEntry?                      _selectedEntry;
        [ObservableProperty] private ObservableCollection<string>               _categories  = new();
        [ObservableProperty] private string?                                    _selectedCategory;
        [ObservableProperty] private string                                     _searchText  = "";

        // ── Loaded template ───────────────────────────────────────────────────
        [ObservableProperty] private ApextTemplate?          _currentTemplate;
        [ObservableProperty] private TemplatePageDefinition? _currentPage;
        [ObservableProperty] private int                      _currentPageIndex;
        [ObservableProperty] private int                      _totalPages;

        // ── Template metadata edits ───────────────────────────────────────────
        [ObservableProperty] private string _editTemplateName        = "";
        [ObservableProperty] private string _editTemplateCategory    = "";
        [ObservableProperty] private string _editTemplateDescription = "";
        [ObservableProperty] private string _selectedPageSize        = "A4";

        // ── Canvas ────────────────────────────────────────────────────────────
        [ObservableProperty] private ObservableCollection<CanvasSlotItem> _canvasSlots     = new();
        [ObservableProperty] private CanvasSlotItem?                      _selectedCanvasSlot;
        [ObservableProperty] private double                               _canvasWidthPt   = 595;
        [ObservableProperty] private double                               _canvasHeightPt  = 842;
        [ObservableProperty] private ImageSource?                        _backgroundSource;

        // ── Selected slot (driven from canvas selection) ──────────────────────
        [ObservableProperty] private TemplateSlotDefinition? _selectedSlot;

        // ── Slot edit fields ──────────────────────────────────────────────────
        [ObservableProperty] private string        _slotName         = "";
        [ObservableProperty] private string        _slotVariableName = "";
        [ObservableProperty] private string        _slotContent      = "";
        [ObservableProperty] private double        _slotFontSize     = 12;
        [ObservableProperty] private string        _slotFontName     = "Tahoma";
        [ObservableProperty] private bool          _slotIsBold;
        [ObservableProperty] private bool          _slotIsItalic;
        [ObservableProperty] private double        _slotX;
        [ObservableProperty] private double        _slotY;
        [ObservableProperty] private double        _slotW            = 60;
        [ObservableProperty] private double        _slotH            = 15;
        [ObservableProperty] private SlotDataType  _slotDataType     = SlotDataType.Text;
        [ObservableProperty] private string        _slotTextColor    = "#000000";
        [ObservableProperty] private bool          _slotIsRtl        = true;
        [ObservableProperty] private HorizontalAlign _slotTextAlign  = HorizontalAlign.Right;
        [ObservableProperty] private string        _slotFormatString = "";

        // ── Status ────────────────────────────────────────────────────────────
        [ObservableProperty] private string _statusMessage = "";
        [ObservableProperty] private string _statusColor   = "#22C55E";
        [ObservableProperty] private bool   _isBusy;
        [ObservableProperty] private bool   _hasTemplate;
        [ObservableProperty] private bool   _hasSlot;

        // 1-based page display (CurrentPageIndex is 0-based)
        public int CurrentPageDisplayIndex => CurrentPageIndex + 1;
        partial void OnCurrentPageIndexChanged(int value) =>
            OnPropertyChanged(nameof(CurrentPageDisplayIndex));

        // ── Enum & font collections ───────────────────────────────────────────
        public SlotDataType[]    AvailableDataTypes  => (SlotDataType[])   Enum.GetValues(typeof(SlotDataType));
        public HorizontalAlign[] AvailableAlignments => (HorizontalAlign[])Enum.GetValues(typeof(HorizontalAlign));
        public string[]          AvailableFonts      => new[]
        {
            "Tahoma", "Arial", "Times New Roman", "Segoe UI", "Calibri",
            "Courier New", "Traditional Arabic", "Simplified Arabic", "Cairo"
        };
        public string[] PageSizePresets => PagePresets.Keys.ToArray();

        // ── Init ──────────────────────────────────────────────────────────────
        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            await LoadTemplatesAsync();
        }

        // ── Partial callbacks ─────────────────────────────────────────────────

        partial void OnSelectedEntryChanged(TemplateLibraryEntry? value)
        {
            if (value == null)
            {
                CurrentTemplate   = null;
                CurrentPage       = null;
                HasTemplate       = false;
                _currentAssets    = new(StringComparer.OrdinalIgnoreCase);
                BackgroundSource  = null;
                CanvasSlots.Clear();
                return;
            }
            try
            {
                var (template, assets) = _library.Load(value.Id);
                _currentAssets   = new Dictionary<string, byte[]>(assets, StringComparer.OrdinalIgnoreCase);
                CurrentTemplate  = template;
                CurrentPageIndex = 0;
                TotalPages       = template.Pages?.Count ?? 0;
                CurrentPage      = template.Pages?.FirstOrDefault();
                HasTemplate      = true;
                SelectedSlot     = null;
                SelectedCanvasSlot = null;

                EditTemplateName        = template.Name;
                EditTemplateCategory    = template.Category;
                EditTemplateDescription = template.Description ?? "";

                RefreshCanvas();
                StatusMessage = "";
            }
            catch (Exception ex)
            {
                // Template file is corrupted / not a valid .apext ZIP — clean up automatically
                SetError("تعذّر فتح القالب (ملف تالف) — جارٍ التنظيف التلقائي…");

                var badEntry = value;
                try { _library.Delete(badEntry.Id); } catch { /* best-effort */ }

                // Defer UI changes so we're outside the current binding cycle
                System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    Templates.Remove(badEntry);

                    if (Templates.Any())
                    {
                        SelectedEntry = Templates.First();
                    }
                    else
                    {
                        // Library is empty — recreate sample templates
                        IsBusy = true;
                        await Task.Run(() => _library.CreateSampleTemplates());
                        foreach (var e in _library.GetAll()) Templates.Add(e);
                        IsBusy = false;
                        if (Templates.Any()) SelectedEntry = Templates.First();
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);

                _ = ex; // suppress unused warning
            }
        }

        partial void OnCurrentPageChanged(TemplatePageDefinition? value)
        {
            SelectedSlot       = null;
            SelectedCanvasSlot = null;
            RefreshCanvas();
        }

        partial void OnSelectedCanvasSlotChanged(CanvasSlotItem? value)
        {
            SelectedSlot = value?.Slot;
        }

        partial void OnSelectedSlotChanged(TemplateSlotDefinition? value)
        {
            HasSlot = value != null;
            if (value == null)
            {
                _selectedSlotBoundColumn = null;
                OnPropertyChanged(nameof(SelectedSlotBoundColumn));
                return;
            }

            SlotName         = value.Name          ?? "";
            SlotVariableName = value.VariableName  ?? "";
            SlotContent      = value.DefaultValue  ?? "";
            SlotFontSize     = value.FontSize;
            SlotFontName     = value.FontFamily    ?? "Tahoma";
            SlotIsBold       = value.Bold;
            SlotIsItalic     = value.Italic;
            SlotX            = Math.Round(value.X,      1);
            SlotY            = Math.Round(value.Y,      1);
            SlotW            = Math.Round(value.Width,  1);
            SlotH            = Math.Round(value.Height, 1);
            SlotDataType     = value.DataType;
            SlotTextColor    = value.TextColor     ?? "#000000";
            SlotIsRtl        = value.IsRtl;
            SlotTextAlign    = value.TextAlign;
            SlotFormatString = value.FormatString  ?? "";

            // Sync the "ربط البيانات" binding dropdown in the right panel
            var mapping = Session.Mappings.FirstOrDefault(m => m.FieldId == value.Id);
            _selectedSlotBoundColumn = mapping?.ColumnName;
            OnPropertyChanged(nameof(SelectedSlotBoundColumn));
        }

        partial void OnSelectedCategoryChanged(string? value) => FilterTemplates();

        partial void OnSearchTextChanged(string value) => FilterTemplates();

        // ── Commands ──────────────────────────────────────────────────────────

        [RelayCommand]
        private async Task LoadTemplatesAsync()
        {
            IsBusy = true;
            try
            {
                await Task.Run(() =>
                {
                    // Rebuild index from disk, skipping any corrupted / non-ZIP .apext files
                    _library.Refresh();

                    var all = _library.GetAll();
                    if (!all.Any())
                    {
                        _library.CreateSampleTemplates();
                        all = _library.GetAll();
                    }
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        Templates.Clear();
                        foreach (var e in all) Templates.Add(e);

                        Categories.Clear();
                        Categories.Add("الكل");
                        foreach (var c in _library.GetCategories()) Categories.Add(c);

                        if (Templates.Any() && SelectedEntry == null)
                            SelectedEntry = Templates.First();
                    });
                });
            }
            catch (Exception ex) { SetError($"خطأ في التحميل: {ex.Message}"); }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        private void AddNewTemplate()
        {
            var template   = ApextFileFormat.CreateNew("قالب جديد", "عام");
            template.Description = "قالب فارغ";
            var entry      = _library.Add(template);
            Templates.Add(entry);
            SelectedEntry  = Templates.Last();
            SetSuccess("✅ تم إنشاء قالب جديد");
        }

        // Called from code-behind (file dialog)
        [RelayCommand]
        private void ImportTemplate(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            { SetError("الملف غير موجود"); return; }
            try
            {
                var entry = _library.Add(filePath);
                if (Templates.All(t => t.Id != entry.Id)) Templates.Add(entry);
                SelectedEntry = Templates.FirstOrDefault(t => t.Id == entry.Id);
                SetSuccess($"✅ تم استيراد: {entry.Name}");
            }
            catch (Exception ex)
            {
                string msg = ex is System.IO.InvalidDataException
                             || ex.Message.Contains("Central Directory")
                             || ex.Message.Contains("ZIP")
                    ? "الملف المختار ليس قالب Apex صالحاً — تأكد من اختيار ملف بامتداد .apext"
                    : $"فشل الاستيراد: {ex.Message}";
                SetError(msg);
            }
        }

        [RelayCommand]
        private void DeleteTemplate()
        {
            if (SelectedEntry == null) return;
            var confirm = System.Windows.MessageBox.Show(
                $"هل تريد حذف القالب '{SelectedEntry.Name}'؟",
                "تأكيد الحذف",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);
            if (confirm != System.Windows.MessageBoxResult.Yes) return;

            var toDelete = SelectedEntry;
            var next     = Templates.FirstOrDefault(t => t.Id != toDelete.Id);
            _library.Delete(toDelete.Id);
            Templates.Remove(toDelete);
            SelectedEntry = next;
            SetSuccess("🗑️ تم حذف القالب");
        }

        [RelayCommand]
        private void DuplicateTemplate()
        {
            if (CurrentTemplate == null) return;
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(CurrentTemplate);
                var copy = System.Text.Json.JsonSerializer.Deserialize<ApextTemplate>(json)!;
                copy.Id          = Guid.NewGuid().ToString();
                copy.Name        = CurrentTemplate.Name + " - نسخة";
                copy.CreatedAt   = DateTime.Now;
                copy.ModifiedAt  = DateTime.Now;
                var entry        = _library.Add(copy, _currentAssets);
                Templates.Add(entry);
                SelectedEntry    = Templates.Last();
                SetSuccess("✅ تم نسخ القالب");
            }
            catch (Exception ex) { SetError($"فشل النسخ: {ex.Message}"); }
        }

        [RelayCommand]
        private void ApplyTemplateMeta()
        {
            if (CurrentTemplate == null) return;
            if (!string.IsNullOrWhiteSpace(EditTemplateName))
                CurrentTemplate.Name = EditTemplateName.Trim();
            if (!string.IsNullOrWhiteSpace(EditTemplateCategory))
                CurrentTemplate.Category = EditTemplateCategory.Trim();
            CurrentTemplate.Description = EditTemplateDescription.Trim();
            SetSuccess("✅ تم تحديث بيانات القالب");
        }

        [RelayCommand]
        private void ApplyPageSize()
        {
            if (CurrentPage == null || !PagePresets.TryGetValue(SelectedPageSize, out var sz)) return;
            CurrentPage.WidthMm     = sz.W;
            CurrentPage.HeightMm    = sz.H;
            CurrentPage.Orientation = sz.W > sz.H
                ? PageOrientation.Landscape
                : PageOrientation.Portrait;
            RefreshCanvas();
            SetSuccess($"✅ حجم الصفحة: {SelectedPageSize} ({sz.W}×{sz.H} مم)");
        }

        // Called from code-behind (file dialog)
        [RelayCommand]
        private void SetBackground(string filePath)
        {
            if (CurrentPage == null || !File.Exists(filePath)) return;
            try
            {
                byte[] bytes   = File.ReadAllBytes(filePath);
                string assetId = Path.GetFileName(filePath);
                _currentAssets[assetId]            = bytes;
                CurrentPage.BackgroundImageAssetId = assetId;
                LoadBackgroundPreview();
                SetSuccess("✅ تم تعيين صورة الخلفية");
            }
            catch (Exception ex) { SetError($"فشل تحميل الصورة: {ex.Message}"); }
        }

        [RelayCommand]
        private void RemoveBackground()
        {
            if (CurrentPage == null) return;
            CurrentPage.BackgroundImageAssetId = null;
            BackgroundSource = null;
            SetSuccess("تمت إزالة الخلفية");
        }

        [RelayCommand]
        private void SaveCurrentTemplate()
        {
            if (CurrentTemplate == null) { SetError("لا يوجد قالب محدد"); return; }
            try
            {
                if (!string.IsNullOrWhiteSpace(EditTemplateName))
                    CurrentTemplate.Name = EditTemplateName.Trim();
                if (!string.IsNullOrWhiteSpace(EditTemplateCategory))
                    CurrentTemplate.Category = EditTemplateCategory.Trim();
                CurrentTemplate.Description = EditTemplateDescription.Trim();
                CurrentTemplate.ModifiedAt  = DateTime.Now;

                var newEntry = _library.Add(CurrentTemplate, _currentAssets);

                // Refresh entry in list
                var idx = SelectedEntry != null
                    ? Templates.IndexOf(SelectedEntry)
                    : -1;
                if (idx >= 0) Templates[idx] = newEntry;
                SelectedEntry = Templates.ElementAtOrDefault(idx >= 0 ? idx : 0);

                SetSuccess("✅ تم حفظ القالب بنجاح");
            }
            catch (Exception ex) { SetError($"خطأ في الحفظ: {ex.Message}"); }
        }

        [RelayCommand]
        private void AddSlot()
        {
            if (CurrentPage == null) return;
            CurrentPage.Slots ??= new List<TemplateSlotDefinition>();
            int n    = CurrentPage.Slots.Count + 1;
            var slot = new TemplateSlotDefinition
            {
                Name          = $"حقل {n}",
                VariableName  = $"Field{n}",
                X = 10, Y = 10 + (n - 1) * 18,
                Width = 80,  Height = 15,
                FontFamily    = "Tahoma",
                FontSize      = 12,
                DataType      = SlotDataType.Text,
                IsRtl         = true,
                TextAlign     = HorizontalAlign.Right,
                TextColor     = "#000000"
            };
            CurrentPage.Slots.Add(slot);
            RebuildCanvasSlots();

            // Auto-select the new slot
            SelectedCanvasSlot = CanvasSlots.LastOrDefault();
            SetSuccess($"✅ تمت إضافة {slot.Name}");
        }

        [RelayCommand]
        private void DeleteSlot()
        {
            if (CurrentPage == null || SelectedSlot == null) return;
            CurrentPage.Slots.Remove(SelectedSlot);
            SelectedSlot       = null;
            SelectedCanvasSlot = null;
            RebuildCanvasSlots();
            SetSuccess("🗑️ تم حذف الحقل");
        }

        [RelayCommand]
        private void ApplySlotEdits()
        {
            if (SelectedSlot == null) return;

            SelectedSlot.Name         = SlotName;
            SelectedSlot.VariableName = SlotVariableName;
            SelectedSlot.DefaultValue = SlotContent.Length > 0 ? SlotContent : null;
            SelectedSlot.FontSize     = SlotFontSize;
            SelectedSlot.FontFamily   = SlotFontName;
            SelectedSlot.Bold         = SlotIsBold;
            SelectedSlot.Italic       = SlotIsItalic;
            SelectedSlot.X            = SlotX;
            SelectedSlot.Y            = SlotY;
            SelectedSlot.Width        = SlotW;
            SelectedSlot.Height       = SlotH;
            SelectedSlot.DataType     = SlotDataType;
            SelectedSlot.TextColor    = SlotTextColor;
            SelectedSlot.IsRtl        = SlotIsRtl;
            SelectedSlot.TextAlign    = SlotTextAlign;
            SelectedSlot.FormatString = SlotFormatString.Length > 0 ? SlotFormatString : null;

            var savedRef = SelectedSlot;
            RebuildCanvasSlots();
            SelectedCanvasSlot = CanvasSlots.FirstOrDefault(c => c.Slot == savedRef);
            SetSuccess("✅ تم تطبيق التعديلات");
        }

        [RelayCommand]
        private void PreviousPage()
        {
            if (CurrentTemplate?.Pages == null || CurrentPageIndex <= 0) return;
            CurrentPageIndex--;
            CurrentPage = CurrentTemplate.Pages[CurrentPageIndex];
        }

        [RelayCommand]
        private void NextPage()
        {
            if (CurrentTemplate?.Pages == null || CurrentPageIndex >= CurrentTemplate.Pages.Count - 1) return;
            CurrentPageIndex++;
            CurrentPage = CurrentTemplate.Pages[CurrentPageIndex];
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private void RefreshCanvas()
        {
            if (CurrentPage != null)
            {
                CanvasWidthPt  = CurrentPage.WidthMm  * 2.835;
                CanvasHeightPt = CurrentPage.HeightMm * 2.835;
            }
            RebuildCanvasSlots();
            LoadBackgroundPreview();
        }

        private void RebuildCanvasSlots()
        {
            CanvasSlots.Clear();
            if (CurrentPage?.Slots == null) return;
            foreach (var slot in CurrentPage.Slots)
                CanvasSlots.Add(new CanvasSlotItem { Slot = slot });
        }

        private void LoadBackgroundPreview()
        {
            var page = CurrentPage;
            if (page?.BackgroundImageAssetId == null ||
                !_currentAssets.TryGetValue(page.BackgroundImageAssetId, out byte[]? bytes))
            {
                BackgroundSource = null;
                return;
            }
            try
            {
                using var ms = new MemoryStream(bytes);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption  = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                BackgroundSource = bmp;
            }
            catch { BackgroundSource = null; }
        }

        private void FilterTemplates()
        {
            IReadOnlyList<TemplateLibraryEntry> base_ =
                !string.IsNullOrWhiteSpace(SearchText)
                    ? _library.Search(SearchText)
                    : string.IsNullOrEmpty(SelectedCategory) || SelectedCategory == "الكل"
                        ? _library.GetAll()
                        : _library.GetByCategory(SelectedCategory);

            Templates.Clear();
            foreach (var e in base_) Templates.Add(e);
        }

        private void SetSuccess(string msg) { StatusMessage = msg; StatusColor = "#22C55E"; }
        private void SetError(string msg)   { StatusMessage = msg; StatusColor = "#EF4444"; }

        // ═══════════════════════════════════════════════════════════════════════
        // SMART VARIABLES INTEGRATION
        // Shared session connects Canvas (Step 0) with all Smart Variables steps.
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Single source of truth for Canvas fields + pasted data + mappings + images.
        /// Shared by reference with SmartVariablesViewModel.
        /// </summary>
        public TemplateDesignerSession Session { get; }

        /// <summary>Sub-ViewModel owning all Smart Variables tabs.</summary>
        public SmartVariablesViewModel SmartVars { get; }

        // ── Step navigation (0 = Design canvas, 1-5 = Smart Variables steps) ──
        [ObservableProperty] private int _activeStep = 0;
        public bool IsDesignStep => ActiveStep == 0;
        public bool IsSmartStep  => ActiveStep > 0;

        // ── Binding section: column bound to currently selected canvas slot ───
        [ObservableProperty] private string? _selectedSlotBoundColumn;

        // Constructor — wires Session into SmartVars
        public TemplateDesignerViewModel()
        {
            Session   = new TemplateDesignerSession();
            SmartVars = new SmartVariablesViewModel();
            SmartVars.AttachSession(Session);
        }

        partial void OnActiveStepChanged(int value)
        {
            OnPropertyChanged(nameof(IsDesignStep));
            OnPropertyChanged(nameof(IsSmartStep));
            if (value > 0)
            {
                SyncFieldsToSession();
                SmartVars.ActiveTabIndex = value - 1;
            }
        }

        partial void OnSelectedSlotBoundColumnChanged(string? value)
        {
            if (SelectedSlot == null || !Session.HasData) return;

            // Update or create a mapping for this slot in the session
            var existing = Session.Mappings.FirstOrDefault(m => m.FieldId == SelectedSlot.Id);
            if (existing != null)
            {
                existing.ColumnName = value;
                existing.Confidence = string.IsNullOrEmpty(value)
                    ? MappingConfidence.None
                    : MappingConfidence.High;
            }
            else if (!string.IsNullOrEmpty(value))
            {
                Session.Mappings.Add(new VariableMapping
                {
                    FieldId    = SelectedSlot.Id,
                    FieldLabel = !string.IsNullOrWhiteSpace(SelectedSlot.Name)
                                    ? SelectedSlot.Name
                                    : (SelectedSlot.VariableName ?? ""),
                    FieldType  = MapSlotType(SelectedSlot.DataType),
                    IsRequired = SelectedSlot.Required,
                    ColumnName = value,
                    Confidence = MappingConfidence.High,
                });
            }

            // Also propagate into SmartVars so mapping tab stays in sync
            SmartVars.SetMapping(SelectedSlot.Id, value);
        }

        // ── Step navigation command ───────────────────────────────────────────

        [RelayCommand]
        private void GoToStep(int step)
        {
            if (Session.IsStepEnabled(step))
                ActiveStep = step;
        }

        // ── Field-type toolbar ────────────────────────────────────────────────

        /// <summary>
        /// Add a new canvas slot of the specified type.
        /// typeStr matches SlotDataType enum names: Text, Number, Date, Image, QrCode, Barcode, Counter.
        /// </summary>
        [RelayCommand]
        private void AddFieldOfType(string typeStr)
        {
            if (CurrentPage == null) return;
            if (!Enum.TryParse<SlotDataType>(typeStr, out var dataType))
                dataType = SlotDataType.Text;

            CurrentPage.Slots ??= new List<TemplateSlotDefinition>();
            int n = CurrentPage.Slots.Count + 1;

            string label = dataType switch
            {
                SlotDataType.Text    => $"نص {n}",
                SlotDataType.Number  => $"رقم {n}",
                SlotDataType.Date    => $"تاريخ {n}",
                SlotDataType.Image   => $"صورة {n}",
                SlotDataType.QrCode  => $"QR {n}",
                SlotDataType.Barcode => $"باركود {n}",
                SlotDataType.Counter => $"مسلسل {n}",
                _                    => $"حقل {n}",
            };

            var slot = new TemplateSlotDefinition
            {
                Name         = label,
                VariableName = $"var{n}",
                X = 10, Y = 10 + (n - 1) * 18,
                Width = 80, Height = 15,
                FontFamily   = "Tahoma",
                FontSize     = 12,
                DataType     = dataType,
                IsRtl        = true,
                TextAlign    = HorizontalAlign.Right,
                TextColor    = "#000000",
            };

            CurrentPage.Slots.Add(slot);
            RebuildCanvasSlots();
            SelectedCanvasSlot = CanvasSlots.LastOrDefault();
            SetSuccess($"✅ تمت إضافة {label}");
        }

        // ── Open / close (for legacy button + keyboard shortcut) ─────────────

        [RelayCommand]
        private void OpenSmartPanel() => GoToStep(1);

        [RelayCommand]
        private void CloseSmartPanel() => ActiveStep = 0;

        // ── Save with Smart Variables data ────────────────────────────────────

        [RelayCommand]
        private void SaveWithSmartData()
        {
            if (CurrentTemplate == null || string.IsNullOrWhiteSpace(SelectedEntry?.FilePath)) return;
            try
            {
                var state = SmartVars.SnapshotState();
                ApextFileFormat.SaveWithSmartData(CurrentTemplate, state, SelectedEntry.FilePath, _currentAssets);
                SetSuccess("✓ تم الحفظ مع بيانات المتغيرات الذكية");
            }
            catch (Exception ex)
            {
                SetError($"خطأ في الحفظ: {ex.Message}");
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Push current canvas slots into the shared session so Smart Variables
        /// steps (Mapping, Preview, etc.) see up-to-date field definitions.
        /// </summary>
        private void SyncFieldsToSession()
        {
            if (CurrentPage?.Slots == null) return;
            var fields = CurrentPage.Slots.Select(s => new SmartTemplateField
            {
                Id          = s.Id,
                Label       = !string.IsNullOrWhiteSpace(s.Name) ? s.Name : (s.VariableName ?? "حقل"),
                VariableKey = s.VariableName ?? "",
                FieldType   = MapSlotType(s.DataType),
                IsRequired  = s.Required,
            }).ToList();

            Session.SyncFields(fields);
            SmartVars.SetTemplateFields(fields);
        }

        private static SmartFieldType MapSlotType(SlotDataType dt) => dt switch
        {
            SlotDataType.Text    => SmartFieldType.TextVariable,
            SlotDataType.Number  => SmartFieldType.NumberVariable,
            SlotDataType.Date    => SmartFieldType.DateVariable,
            SlotDataType.Image   => SmartFieldType.ImageVariable,
            SlotDataType.Barcode => SmartFieldType.Barcode,
            SlotDataType.QrCode  => SmartFieldType.QRCode,
            SlotDataType.Counter => SmartFieldType.AutoSerial,
            _                    => SmartFieldType.TextVariable,
        };
    }
}
