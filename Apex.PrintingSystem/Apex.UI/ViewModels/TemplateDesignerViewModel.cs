using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Apex.Core.Utilities;
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
using System.Windows.Threading;

namespace Apex.UI.ViewModels
{
    // ── Canvas slot wrapper ───────────────────────────────────────────────────
    /// <summary>
    /// Wraps a TemplateSlotDefinition for Canvas display.
    /// Extends ObservableObject so that SetPosition() raises PropertyChanged
    /// and live-updates the Canvas.Left / Canvas.Top bindings while dragging.
    /// </summary>
    public class CanvasSlotItem : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        /// <summary>Minimum on-screen size so a tiny slot stays selectable (≈4.2 mm).</summary>
        private const double MinSizeDip = 16;

        public TemplateSlotDefinition Slot { get; init; } = null!;

        // All four properties are computed from Slot fields (mm) into canvas DIPs —
        // they notify on demand.
        public double Left => UnitConverter.MmToDips(Slot.X);
        public double Top => UnitConverter.MmToDips(Slot.Y);
        public double Width => Math.Max(UnitConverter.MmToDips(Slot.Width), MinSizeDip);
        public double Height => Math.Max(UnitConverter.MmToDips(Slot.Height), MinSizeDip);

        /// <summary>
        /// Update position from a drag operation (canvas DIPs → slot mm).
        /// Raises PropertyChanged for Left and Top so the Canvas.Left/Top bindings
        /// on the ListBoxItem container refresh immediately.
        /// </summary>
        public void SetPosition(double leftDip, double topDip)
        {
            Slot.X = Math.Max(0, UnitConverter.DipsToMm(leftDip));
            Slot.Y = Math.Max(0, UnitConverter.DipsToMm(topDip));
            OnPropertyChanged(nameof(Left));
            OnPropertyChanged(nameof(Top));
        }

        /// <summary>
        /// Update size from a resize operation (canvas DIPs → slot mm).
        /// Raises PropertyChanged for Width and Height so Canvas bindings refresh.
        /// </summary>
        public void SetSize(double widthDip, double heightDip)
        {
            Slot.Width = Math.Max(1, UnitConverter.DipsToMm(widthDip));
            Slot.Height = Math.Max(1, UnitConverter.DipsToMm(heightDip));
            OnPropertyChanged(nameof(Width));
            OnPropertyChanged(nameof(Height));
        }

        /// <summary>Opacity for canvas preview (clamped to a visible minimum of 0.15).</summary>
        public double OpacityValue => Math.Max(0.15, Math.Min(1.0, Slot.Opacity));

        /// <summary>Rotation angle for canvas preview.</summary>
        public double RotationAngle => Slot.RotationDegrees;

        // ── Multi-select visual state ─────────────────────────────────────
        private bool _isMultiSelected;
        public bool IsMultiSelected
        {
            get => _isMultiSelected;
            set { if (_isMultiSelected != value) { _isMultiSelected = value; OnPropertyChanged(nameof(IsMultiSelected)); } }
        }

        /// <summary>Re-reads geometry from Slot and notifies all position/size bindings.</summary>
        public void RefreshFromSlot()
        {
            OnPropertyChanged(nameof(Left));
            OnPropertyChanged(nameof(Top));
            OnPropertyChanged(nameof(Width));
            OnPropertyChanged(nameof(Height));
        }

        public string Label => string.IsNullOrWhiteSpace(Slot.Name) ? (Slot.VariableName ?? "?") : Slot.Name;
        public string TypeTag => Slot.DataType switch
        {
            SlotDataType.Text => ViewModelBase.L("Des_TagText"),
            SlotDataType.Number => ViewModelBase.L("Des_TagNum"),
            SlotDataType.Date => ViewModelBase.L("Des_TagDate"),
            SlotDataType.Image => ViewModelBase.L("Des_TagImg"),
            SlotDataType.Barcode => ViewModelBase.L("Des_TagBar"),
            SlotDataType.QrCode => "QR",
            SlotDataType.Counter => ViewModelBase.L("Des_TagCounter"),
            _ => ViewModelBase.L("Des_TagUnknown")
        };

        public string BorderColor => Slot.DataType switch
        {
            SlotDataType.Text => "#3B82F6",
            SlotDataType.Number => "#10B981",
            SlotDataType.Date => "#F59E0B",
            SlotDataType.Image => "#8B5CF6",
            SlotDataType.Barcode => "#EF4444",
            SlotDataType.QrCode => "#EF4444",
            SlotDataType.Counter => "#06B6D4",
            _ => "#3B82F6"
        };

        public string FillColor => Slot.DataType switch
        {
            SlotDataType.Text => "#193B82F6",
            SlotDataType.Number => "#1910B981",
            SlotDataType.Date => "#19F59E0B",
            SlotDataType.Image => "#198B5CF6",
            SlotDataType.Barcode => "#19EF4444",
            SlotDataType.QrCode => "#19EF4444",
            SlotDataType.Counter => "#1906B6D4",
            _ => "#193B82F6"
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
            ["A3"] = (297, 420),
            ["A4"] = (210, 297),
            ["A5"] = (148, 210),
            ["A6"] = (105, 148),
            ["SRA3"] = (320, 450),
            ["تابلويد (Tabloid)"] = (279.4, 431.8),
            ["رسالة (Letter)"] = (215.9, 279.4),
            ["ليجال (Legal)"] = (215.9, 355.6),
            ["بطاقة عمل"] = (85.6, 54),
            ["ملصق"] = (101.6, 63.5),
            ["مخصص"] = (0, 0),   // sentinel: use CustomWidthMm × CustomHeightMm
        };

        /// <summary>The preset key that triggers custom width/height inputs.</summary>
        private const string CustomSizeKey = "مخصص";

        // ── Library ───────────────────────────────────────────────────────────
        [ObservableProperty] private ObservableCollection<TemplateLibraryEntry> _templates = new();
        [ObservableProperty] private TemplateLibraryEntry? _selectedEntry;
        [ObservableProperty] private ObservableCollection<string> _categories = new();
        [ObservableProperty] private string? _selectedCategory;
        [ObservableProperty] private string _searchText = "";

        // ── Loaded template ───────────────────────────────────────────────────
        [ObservableProperty] private ApextTemplate? _currentTemplate;
        [ObservableProperty] private TemplatePageDefinition? _currentPage;
        [ObservableProperty] private int _currentPageIndex;
        [ObservableProperty] private int _totalPages;

        // ── Template metadata edits ───────────────────────────────────────────
        [ObservableProperty] private string _editTemplateName = "";
        [ObservableProperty] private string _editTemplateCategory = "";
        [ObservableProperty] private string _editTemplateDescription = "";
        [ObservableProperty] private string _selectedPageSize = "A4";

        // ── Custom page size (mm) — used when SelectedPageSize == "مخصص" ──────
        [ObservableProperty] private double _customWidthMm = 210;
        [ObservableProperty] private double _customHeightMm = 297;
        /// <summary>True when the custom-size inputs should be shown.</summary>
        public bool IsCustomPageSize => SelectedPageSize == CustomSizeKey;
        partial void OnSelectedPageSizeChanged(string value) =>
            OnPropertyChanged(nameof(IsCustomPageSize));

        // ── Canvas ────────────────────────────────────────────────────────────
        [ObservableProperty] private ObservableCollection<CanvasSlotItem> _canvasSlots = new();
        [ObservableProperty] private CanvasSlotItem? _selectedCanvasSlot;
        [ObservableProperty] private double _canvasWidthDip = 595;
        [ObservableProperty] private double _canvasHeightDip = 842;
        [ObservableProperty] private ImageSource? _backgroundSource;

        // ── Selected slot (driven from canvas selection) ──────────────────────
        [ObservableProperty] private TemplateSlotDefinition? _selectedSlot;

        // ── Slot edit fields ──────────────────────────────────────────────────
        [ObservableProperty] private string _slotName = "";
        [ObservableProperty] private string _slotVariableName = "";
        [ObservableProperty] private string _slotContent = "";
        [ObservableProperty] private double _slotFontSize = 12;
        [ObservableProperty] private string _slotFontName = "Tahoma";
        [ObservableProperty] private bool _slotIsBold;
        [ObservableProperty] private bool _slotIsItalic;
        [ObservableProperty] private double _slotX;
        [ObservableProperty] private double _slotY;
        [ObservableProperty] private double _slotW = 60;
        [ObservableProperty] private double _slotH = 15;
        [ObservableProperty] private SlotDataType _slotDataType = SlotDataType.Text;
        [ObservableProperty] private string _slotTextColor = "#000000";
        [ObservableProperty] private bool _slotIsRtl = true;
        [ObservableProperty] private HorizontalAlign _slotTextAlign = HorizontalAlign.Right;
        [ObservableProperty] private VerticalAlign _slotVerticalAlign = VerticalAlign.Middle;
        [ObservableProperty] private string _slotFormatString = "";
        [ObservableProperty] private bool _slotRequired = false;
        // Extra appearance properties
        [ObservableProperty] private int    _slotRotation = 0;
        [ObservableProperty] private double _slotOpacity = 1.0;
        [ObservableProperty] private string _slotBgColor = "Transparent";
        [ObservableProperty] private string _slotImageFitMode = "Contain";

        // ── Multi-select ──────────────────────────────────────────────────
        [ObservableProperty] private int _selectionCount;
        public bool HasMultiSelection => SelectionCount > 1;
        /// <summary>Populated by code-behind Ctrl+Click handler.</summary>
        internal readonly List<CanvasSlotItem> MultiSelectedSlots = new();

        // ── Clipboard ─────────────────────────────────────────────────────
        private List<TemplateSlotDefinition>? _clipboardSlots;
        public bool HasClipboard => _clipboardSlots?.Count > 0;

        // ── Status ────────────────────────────────────────────────────────────
        [ObservableProperty] private string _statusMessage = "";
        [ObservableProperty] private string _statusColor = "#22C55E";
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private bool _hasTemplate;
        [ObservableProperty] private bool _hasSlot;
        /// <summary>True when the canvas has unsaved changes.</summary>
        [ObservableProperty] private bool _isDirty;

        // ── Undo / Redo ───────────────────────────────────────────────────────
        // Two-stack model: PushUndo() (called BEFORE each mutation) records the
        // pre-edit snapshot on the undo stack and clears redo. Undo/Redo capture
        // the CURRENT live state before restoring, so the latest edit is never
        // lost (the previous single-list/index scheme dropped it → broken redo).
        private static readonly System.Text.Json.JsonSerializerOptions _undoJsonOpts = new()
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };
        private readonly Stack<string> _undoStack = new();
        private readonly Stack<string> _redoStack = new();
        private const int MaxUndoSteps = 50;
        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        // ── Grid snap ─────────────────────────────────────────────────────────
        [ObservableProperty] private bool _isSnapEnabled;
        [ObservableProperty] private double _gridSizeMm = 5.0;

        // ── Color presets ─────────────────────────────────────────────────────
        public static readonly string[] PresetColors =
        {
            "#000000", "#FFFFFF", "#EF4444", "#F97316", "#F59E0B", "#EAB308",
            "#22C55E", "#14B8A6", "#3B82F6", "#6366F1", "#8B5CF6", "#EC4899",
            "#6B7280", "#1E293B", "#334155", "Transparent"
        };

        // ── Zoom ──────────────────────────────────────────────────────────────
        // ZoomBoost is a multiplier applied ON TOP of the Viewbox "fit" scaling.
        // 1.0 = fit to window (default), 1.25 = 25% larger than fit, etc.
        [ObservableProperty] private double _zoomBoost = 1.0;
        public string ZoomBoostLabel => $"{(int)Math.Round(ZoomBoost * 100)}%";
        partial void OnZoomBoostChanged(double value) => OnPropertyChanged(nameof(ZoomBoostLabel));

        // ── Canvas status bar ─────────────────────────────────────────────────
        // Shows selected slot position/size, or total slot count.
        public string CanvasStatusText
        {
            get
            {
                if (SelectionCount > 1)
                    return Lf("Des_SelectedFields", SelectionCount);
                if (HasSlot)
                    return Lf("Des_SlotCoords", SlotX, SlotY, SlotW, SlotH);
                if (CanvasSlots.Count > 0)
                    return Lf("Des_FieldsOnPage", CanvasSlots.Count);
                return L("Des_PageEmpty");
            }
        }

        // Step completion tracking
        [ObservableProperty] private bool _step0Complete;

        private void NotifyCanvasStatus()
        {
            OnPropertyChanged(nameof(CanvasStatusText));
        }

        // 1-based page display (CurrentPageIndex is 0-based)
        public int CurrentPageDisplayIndex => CurrentPageIndex + 1;
        partial void OnCurrentPageIndexChanged(int value) =>
            OnPropertyChanged(nameof(CurrentPageDisplayIndex));

        // ── Enum & font collections ───────────────────────────────────────────
        public SlotDataType[] AvailableDataTypes => (SlotDataType[])Enum.GetValues(typeof(SlotDataType));
        public HorizontalAlign[] AvailableAlignments => (HorizontalAlign[])Enum.GetValues(typeof(HorizontalAlign));
        // All installed system fonts (lazy-loaded once, alphabetically sorted)
        private static readonly Lazy<string[]> _systemFonts = new(() =>
            Fonts.SystemFontFamilies
                 .Select(f => f.Source)
                 .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                 .ToArray());
        public string[] AvailableFonts => _systemFonts.Value;
        public VerticalAlign[] AvailableVerticalAlignments => (VerticalAlign[])Enum.GetValues(typeof(VerticalAlign));
        public string[] AvailableImageFitModes => new[] { "Contain", "Cover", "Stretch", "Fill" };
        public bool IsImageSlot => SlotDataType == SlotDataType.Image;
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
                CurrentTemplate = null;
                CurrentPage = null;
                HasTemplate = false;
                _currentAssets = new(StringComparer.OrdinalIgnoreCase);
                BackgroundSource = null;
                CanvasSlots.Clear();
                return;
            }
            try
            {
                var (template, assets) = _library.Load(value.Id);
                _currentAssets = new Dictionary<string, byte[]>(assets, StringComparer.OrdinalIgnoreCase);
                CurrentTemplate = template;
                CurrentPageIndex = 0;
                TotalPages = template.Pages?.Count ?? 0;
                CurrentPage = template.Pages?.FirstOrDefault();
                HasTemplate = true;
                SelectedSlot = null;
                SelectedCanvasSlot = null;

                EditTemplateName = template.Name;
                EditTemplateCategory = template.Category;
                EditTemplateDescription = template.Description ?? "";

                RefreshCanvas();
                ResetUndoHistory();
                StatusMessage = "";
            }
            catch (Exception ex)
            {
                // Template file is corrupted / not a valid .apext ZIP — clean up automatically
                SetError(L("Des_CorruptCleaning"));

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
            SelectedSlot = null;
            SelectedCanvasSlot = null;
            RefreshCanvas();
            ResetUndoHistory();
        }

        partial void OnSelectedCanvasSlotChanged(CanvasSlotItem? value)
        {
            SelectedSlot = value?.Slot;
        }

        partial void OnSelectedSlotChanged(TemplateSlotDefinition? value)
        {
            HasSlot = value != null;
            OnPropertyChanged(nameof(IsTextLikeSlot));
            if (value == null)
            {
                _selectedSlotBoundColumn = null;
                OnPropertyChanged(nameof(SelectedSlotBoundColumn));
                return;
            }

            SlotName = value.Name ?? "";
            SlotVariableName = value.VariableName ?? "";
            SlotContent = value.DefaultValue ?? "";
            SlotFontSize = value.FontSize;
            SlotFontName = value.FontFamily ?? "Tahoma";
            SlotIsBold = value.Bold;
            SlotIsItalic = value.Italic;
            SlotX = Math.Round(value.X, 1);
            SlotY = Math.Round(value.Y, 1);
            SlotW = Math.Round(value.Width, 1);
            SlotH = Math.Round(value.Height, 1);
            SlotDataType = value.DataType;
            SlotTextColor = value.TextColor ?? "#000000";
            SlotIsRtl = value.IsRtl;
            SlotTextAlign = value.TextAlign;
            SlotVerticalAlign = value.VerticalAlign;
            SlotFormatString = value.FormatString ?? "";
            SlotRequired = value.Required;
            SlotRotation = value.RotationDegrees;
            SlotOpacity  = value.Opacity;
            SlotBgColor  = string.IsNullOrEmpty(value.BackgroundColor) ? "Transparent" : value.BackgroundColor;
            SlotImageFitMode = value.ImageFitMode ?? "Contain";

            // Sync the L("Des_DataBinding") binding dropdown in the right panel
            var mapping = Session.Mappings.FirstOrDefault(m => m.FieldId == value.Id);
            _selectedSlotBoundColumn = mapping?.ColumnName;
            OnPropertyChanged(nameof(SelectedSlotBoundColumn));
        }

        /// <summary>
        /// True when the selected slot type uses text-formatting properties
        /// (Font, Bold/Italic, RTL, Alignment, Colour, Format).
        /// False for Image / QrCode / Barcode where those properties are irrelevant.
        /// Bound to the text-properties group visibility in the properties panel.
        /// </summary>
        public bool IsTextLikeSlot => SlotDataType is
            SlotDataType.Text or SlotDataType.Number or SlotDataType.Date or SlotDataType.Counter;

        /// Refresh IsTextLikeSlot whenever the user changes the type dropdown.
        partial void OnSlotDataTypeChanged(SlotDataType value)
        {
            OnPropertyChanged(nameof(IsTextLikeSlot));
            OnPropertyChanged(nameof(IsImageSlot));
        }

        partial void OnSelectedCategoryChanged(string? value) => FilterTemplates();

        partial void OnSearchTextChanged(string value) => FilterTemplates();

        partial void OnSlotXChanged(double value) => NotifyCanvasStatus();
        partial void OnSlotYChanged(double value) => NotifyCanvasStatus();
        partial void OnSlotWChanged(double value) => NotifyCanvasStatus();
        partial void OnSlotHChanged(double value) => NotifyCanvasStatus();
        partial void OnHasSlotChanged(bool value) => NotifyCanvasStatus();
        partial void OnHasTemplateChanged(bool value) { Step0Complete = value; }
        partial void OnSelectionCountChanged(int value)
        {
            OnPropertyChanged(nameof(HasMultiSelection));
            NotifyCanvasStatus();
        }

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
                        Categories.Add(L("Des_All"));
                        foreach (var c in _library.GetCategories()) Categories.Add(c);

                        if (Templates.Any() && SelectedEntry == null)
                            SelectedEntry = Templates.First();
                    });
                });
            }
            catch (Exception ex) { SetError(Lf("Des_LoadError", ex.Message)); }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        private void AddNewTemplate()
        {
            var template = ApextFileFormat.CreateNew(L("Des_NewTemplate"), L("Des_General"));
            template.Description = L("Des_EmptyTemplate");
            var entry = _library.Add(template);
            Templates.Add(entry);
            SelectedEntry = Templates.Last();
            SetSuccess(L("Des_NewCreated"));
        }

        // Called from code-behind (file dialog)
        [RelayCommand]
        private void ImportTemplate(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            { SetError(L("Des_FileMissing")); return; }
            try
            {
                var entry = _library.Add(filePath);
                if (Templates.All(t => t.Id != entry.Id)) Templates.Add(entry);
                SelectedEntry = Templates.FirstOrDefault(t => t.Id == entry.Id);
                SetSuccess(Lf("Des_Imported", entry.Name));
            }
            catch (Exception ex)
            {
                string msg = ex is System.IO.InvalidDataException
                             || ex.Message.Contains("Central Directory")
                             || ex.Message.Contains("ZIP")
                    ? L("Des_InvalidApext")
                    : Lf("Des_ImportFailed", ex.Message);
                SetError(msg);
            }
        }

        [RelayCommand]
        private void DeleteTemplate()
        {
            if (SelectedEntry == null) return;
            var confirm = System.Windows.MessageBox.Show(
                Lf("Des_ConfirmDeleteTemplate", SelectedEntry.Name),
                L("Des_ConfirmDeleteTitle"),
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);
            if (confirm != System.Windows.MessageBoxResult.Yes) return;

            var toDelete = SelectedEntry;
            var next = Templates.FirstOrDefault(t => t.Id != toDelete.Id);
            _library.Delete(toDelete.Id);
            Templates.Remove(toDelete);
            SelectedEntry = next;
            SetSuccess(L("Des_TemplateDeleted"));
        }

        [RelayCommand]
        private void DuplicateTemplate()
        {
            if (CurrentTemplate == null) return;
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(CurrentTemplate);
                var copy = System.Text.Json.JsonSerializer.Deserialize<ApextTemplate>(json)!;
                copy.Id = Guid.NewGuid().ToString();
                copy.Name = CurrentTemplate.Name + L("Des_CopySuffix");
                copy.CreatedAt = DateTime.Now;
                copy.ModifiedAt = DateTime.Now;
                var entry = _library.Add(copy, _currentAssets);
                Templates.Add(entry);
                SelectedEntry = Templates.Last();
                SetSuccess(L("Des_TemplateCopied"));
            }
            catch (Exception ex) { SetError(Lf("Des_CopyFailed", ex.Message)); }
        }

        // Sensible bounds for a page dimension in millimetres.
        private const double MinPageMm = 10.0;
        private const double MaxPageMm = 2000.0;   // up to 2 m for large-format work

        [RelayCommand]
        private void ApplyPageSize()
        {
            if (CurrentPage == null) return;

            double w, h;
            if (SelectedPageSize == CustomSizeKey)
            {
                // Custom: take the user-entered millimetre values, clamped to sane bounds.
                if (CustomWidthMm <= 0 || CustomHeightMm <= 0)
                {
                    SetError(L("Des_CustomSizePositive"));
                    return;
                }
                w = Math.Clamp(CustomWidthMm, MinPageMm, MaxPageMm);
                h = Math.Clamp(CustomHeightMm, MinPageMm, MaxPageMm);
            }
            else if (PagePresets.TryGetValue(SelectedPageSize, out var sz) && sz.W > 0)
            {
                w = sz.W;
                h = sz.H;
            }
            else
            {
                return;
            }

            PushUndo();
            CurrentPage.WidthMm = w;
            CurrentPage.HeightMm = h;
            CurrentPage.Orientation = w > h
                ? PageOrientation.Landscape
                : PageOrientation.Portrait;
            RefreshCanvas();
            IsDirty = true;
            SetSuccess(Lf("Des_PageSizeSet", SelectedPageSize, w, h));
        }

        // Called from code-behind (file dialog)
        [RelayCommand]
        private void SetBackground(string filePath)
        {
            if (CurrentPage == null || !File.Exists(filePath)) return;
            PushUndo();
            try
            {
                byte[] bytes = File.ReadAllBytes(filePath);
                string assetId = Path.GetFileName(filePath);
                _currentAssets[assetId] = bytes;
                CurrentPage.BackgroundImageAssetId = assetId;
                LoadBackgroundPreview();
                IsDirty = true;
                SetSuccess(L("Des_BgSet"));
            }
            catch (Exception ex) { SetError(Lf("Des_ImgLoadFailed", ex.Message)); }
        }

        [RelayCommand]
        private void RemoveBackground()
        {
            if (CurrentPage == null) return;
            PushUndo();
            CurrentPage.BackgroundImageAssetId = null;
            BackgroundSource = null;
            IsDirty = true;
            SetSuccess(L("Des_BgRemoved"));
        }

        [RelayCommand]
        private void SaveCurrentTemplate()
        {
            if (CurrentTemplate == null) { SetError(L("Des_NoTemplateSelected")); return; }
            try
            {
                // Apply metadata fields (merged — no separate Apply button needed)
                if (!string.IsNullOrWhiteSpace(EditTemplateName))
                    CurrentTemplate.Name = EditTemplateName.Trim();
                if (!string.IsNullOrWhiteSpace(EditTemplateCategory))
                    CurrentTemplate.Category = EditTemplateCategory.Trim();
                CurrentTemplate.Description = EditTemplateDescription.Trim();
                CurrentTemplate.ModifiedAt = DateTime.Now;

                var newEntry = _library.Add(CurrentTemplate, _currentAssets);

                var idx = SelectedEntry != null ? Templates.IndexOf(SelectedEntry) : -1;
                if (idx >= 0) Templates[idx] = newEntry;
                SelectedEntry = Templates.ElementAtOrDefault(idx >= 0 ? idx : 0);

                IsDirty = false;
                Step0Complete = true;
                SetSuccess(L("Des_SavedOk"));
                GoToStep("1");
            }
            catch (Exception ex) { SetError(Lf("Des_SaveError", ex.Message)); }
        }

        [RelayCommand]
        private void AddPage()
        {
            if (CurrentTemplate?.Pages == null) return;
            PushUndo();
            var newPage = new TemplatePageDefinition
            {
                WidthMm = CurrentPage?.WidthMm ?? 210,
                HeightMm = CurrentPage?.HeightMm ?? 297,
                Orientation = CurrentPage?.Orientation ?? PageOrientation.Portrait,
                Slots = new List<TemplateSlotDefinition>()
            };
            CurrentTemplate.Pages.Add(newPage);
            TotalPages = CurrentTemplate.Pages.Count;
            CurrentPageIndex = CurrentTemplate.Pages.Count - 1;
            CurrentPage = newPage;
            IsDirty = true;
            SetSuccess(Lf("Des_PageAdded", TotalPages));
        }

        [RelayCommand]
        private void DeletePage()
        {
            if (CurrentTemplate?.Pages == null || TotalPages <= 1) return;
            PushUndo();
            var confirm = System.Windows.MessageBox.Show(
                Lf("Des_ConfirmDeletePage", CurrentPageDisplayIndex),
                L("Des_ConfirmDeletePageTitle"),
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);
            if (confirm != System.Windows.MessageBoxResult.Yes) return;

            CurrentTemplate.Pages.RemoveAt(CurrentPageIndex);
            TotalPages = CurrentTemplate.Pages.Count;
            CurrentPageIndex = Math.Min(CurrentPageIndex, TotalPages - 1);
            CurrentPage = CurrentTemplate.Pages[CurrentPageIndex];
            IsDirty = true;
            SetSuccess(L("Des_PageDeleted"));
        }

        [RelayCommand]
        private void AddSlot()
        {
            if (CurrentPage == null) return;
            PushUndo();
            CurrentPage.Slots ??= new List<TemplateSlotDefinition>();
            int n = CurrentPage.Slots.Count + 1;
            var slot = new TemplateSlotDefinition
            {
                Name = Lf("Des_FieldN", n),
                VariableName = $"Field{n}",
                X = 10,
                Y = 10 + (n - 1) * 18,
                Width = 80,
                Height = 15,
                FontFamily = "Tahoma",
                FontSize = 12,
                DataType = SlotDataType.Text,
                IsRtl = true,
                TextAlign = HorizontalAlign.Right,
                TextColor = "#000000"
            };
            CurrentPage.Slots.Add(slot);
            RebuildCanvasSlots();
            SelectedCanvasSlot = CanvasSlots.LastOrDefault();
            IsDirty = true;
            SetSuccess(Lf("Des_Added", slot.Name));
        }

        [RelayCommand]
        private void DeleteSlot()
        {
            if (CurrentPage == null || SelectedSlot == null) return;
            PushUndo();
            CurrentPage.Slots.Remove(SelectedSlot);
            SelectedSlot = null;
            SelectedCanvasSlot = null;
            RebuildCanvasSlots();
            IsDirty = true;
            SetSuccess(L("Des_FieldDeleted"));
        }

        [RelayCommand]
        private void ApplySlotEdits()
        {
            if (SelectedSlot == null) return;
            PushUndo();

            SelectedSlot.Name = SlotName;
            SelectedSlot.VariableName = SlotVariableName;
            SelectedSlot.DefaultValue = SlotContent.Length > 0 ? SlotContent : null;
            SelectedSlot.FontSize = SlotFontSize;
            SelectedSlot.FontFamily = SlotFontName;
            SelectedSlot.Bold = SlotIsBold;
            SelectedSlot.Italic = SlotIsItalic;
            SelectedSlot.X = SlotX;
            SelectedSlot.Y = SlotY;
            SelectedSlot.Width = SlotW;
            SelectedSlot.Height = SlotH;
            SelectedSlot.DataType = SlotDataType;
            SelectedSlot.TextColor = SlotTextColor;
            SelectedSlot.IsRtl = SlotIsRtl;
            SelectedSlot.TextAlign = SlotTextAlign;
            SelectedSlot.VerticalAlign = SlotVerticalAlign;
            SelectedSlot.FormatString = SlotFormatString.Length > 0 ? SlotFormatString : null;
            SelectedSlot.Required = SlotRequired;
            SelectedSlot.RotationDegrees = SlotRotation;
            SelectedSlot.Opacity = Math.Clamp(SlotOpacity, 0.0, 1.0);
            SelectedSlot.BackgroundColor = SlotBgColor;
            SelectedSlot.ImageFitMode = SlotImageFitMode;

            var savedRef = SelectedSlot;
            RebuildCanvasSlots();
            SelectedCanvasSlot = CanvasSlots.FirstOrDefault(c => c.Slot == savedRef);
            IsDirty = true;
            SetSuccess(L("Des_ChangesApplied"));
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
                CanvasWidthDip = UnitConverter.MmToDips(CurrentPage.WidthMm);
                CanvasHeightDip = UnitConverter.MmToDips(CurrentPage.HeightMm);
            }
            RebuildCanvasSlots();
            LoadBackgroundPreview();
        }

        private void RebuildCanvasSlots()
        {
            CanvasSlots.Clear();
            if (CurrentPage?.Slots == null) { NotifyCanvasStatus(); return; }
            foreach (var slot in CurrentPage.Slots)
                CanvasSlots.Add(new CanvasSlotItem { Slot = slot });
            NotifyCanvasStatus();
        }

        // ── Zoom commands ─────────────────────────────────────────────────────

        [RelayCommand]
        private void ZoomIn() { ZoomBoost = Math.Min(3.0, Math.Round(ZoomBoost + 0.25, 2)); }

        [RelayCommand]
        private void ZoomOut() { ZoomBoost = Math.Max(0.25, Math.Round(ZoomBoost - 0.25, 2)); }

        [RelayCommand]
        private void FitToWindow() { ZoomBoost = 1.0; }

        // ── Undo / Redo commands ──────────────────────────────────────────────

        [RelayCommand]
        private void Undo()
        {
            if (!CanUndo || CurrentPage == null) return;
            // Save the current (post-edit) state so Redo can return to it.
            var current = SnapshotSlots();
            if (current != null) _redoStack.Push(current);

            RestoreSlotsFromJson(_undoStack.Pop());
            SetHint(L("Des_Undo"));
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
        }

        [RelayCommand]
        private void Redo()
        {
            if (!CanRedo || CurrentPage == null) return;
            // Save the current state so Undo can return to it.
            var current = SnapshotSlots();
            if (current != null) _undoStack.Push(current);

            RestoreSlotsFromJson(_redoStack.Pop());
            SetHint(L("Des_Redo"));
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
        }

        /// <summary>Serialize the current page's slots, or null when there is no page.</summary>
        private string? SnapshotSlots()
        {
            if (CurrentPage?.Slots == null) return null;
            return System.Text.Json.JsonSerializer.Serialize(CurrentPage.Slots, _undoJsonOpts);
        }

        private void RestoreSlotsFromJson(string json)
        {
            CurrentPage!.Slots = System.Text.Json.JsonSerializer
                .Deserialize<List<TemplateSlotDefinition>>(json, _undoJsonOpts)!;
            RebuildCanvasSlots();
            SelectedCanvasSlot = null;
            IsDirty = true;
        }

        /// <summary>Records the pre-edit snapshot. Call BEFORE mutating the slots.</summary>
        private void PushUndo()
        {
            var snap = SnapshotSlots();
            if (snap == null) return;

            _undoStack.Push(snap);

            // Cap history: drop the oldest (bottom-of-stack) snapshots.
            if (_undoStack.Count > MaxUndoSteps)
            {
                var newest = _undoStack.ToArray();          // index 0 = newest
                _undoStack.Clear();
                for (int i = MaxUndoSteps - 1; i >= 0; i--)
                    _undoStack.Push(newest[i]);
            }

            _redoStack.Clear();
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
        }

        private void ResetUndoHistory()
        {
            _undoStack.Clear();
            _redoStack.Clear();
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
        }

        /// <summary>Called by code-behind before a drag or resize starts.</summary>
        public void PushUndoBeforeDrag() => PushUndo();

        /// <summary>Snap a canvas DIP value to the grid (returns original if snap off).</summary>
        public double SnapDip(double valueDip)
        {
            if (!IsSnapEnabled || GridSizeMm <= 0) return valueDip;
            double mm = UnitConverter.DipsToMm(valueDip);
            mm = Math.Round(mm / GridSizeMm) * GridSizeMm;
            return UnitConverter.MmToDips(mm);
        }

        // ── Slot reorder ──────────────────────────────────────────────────────

        [RelayCommand]
        private void MoveSlotUp()
        {
            if (CurrentPage?.Slots == null || SelectedSlot == null) return;
            int idx = CurrentPage.Slots.IndexOf(SelectedSlot);
            if (idx <= 0) return;
            PushUndo();
            (CurrentPage.Slots[idx], CurrentPage.Slots[idx - 1]) = (CurrentPage.Slots[idx - 1], CurrentPage.Slots[idx]);
            var savedSlot = SelectedSlot;
            RebuildCanvasSlots();
            SelectedCanvasSlot = CanvasSlots.FirstOrDefault(c => c.Slot == savedSlot);
            IsDirty = true;
        }

        [RelayCommand]
        private void MoveSlotDown()
        {
            if (CurrentPage?.Slots == null || SelectedSlot == null) return;
            int idx = CurrentPage.Slots.IndexOf(SelectedSlot);
            if (idx < 0 || idx >= CurrentPage.Slots.Count - 1) return;
            PushUndo();
            (CurrentPage.Slots[idx], CurrentPage.Slots[idx + 1]) = (CurrentPage.Slots[idx + 1], CurrentPage.Slots[idx]);
            var savedSlot = SelectedSlot;
            RebuildCanvasSlots();
            SelectedCanvasSlot = CanvasSlots.FirstOrDefault(c => c.Slot == savedSlot);
            IsDirty = true;
        }

        // ── Color preset commands ─────────────────────────────────────────────

        [RelayCommand]
        private void SetTextColorPreset(string color) { SlotTextColor = color; }

        [RelayCommand]
        private void SetBgColorPreset(string color) { SlotBgColor = color; }

        // ── Multi-select management ───────────────────────────────────────

        /// <summary>Toggle a slot in/out of the multi-selection (Ctrl+Click).</summary>
        public void ToggleMultiSelect(CanvasSlotItem item)
        {
            if (MultiSelectedSlots.Contains(item))
            {
                MultiSelectedSlots.Remove(item);
                item.IsMultiSelected = false;
            }
            else
            {
                MultiSelectedSlots.Add(item);
                item.IsMultiSelected = true;
            }
            SelectionCount = MultiSelectedSlots.Count;
        }

        /// <summary>Clear multi-selection state (called on single-click without Ctrl).</summary>
        public void ClearMultiSelect()
        {
            foreach (var s in MultiSelectedSlots) s.IsMultiSelected = false;
            MultiSelectedSlots.Clear();
            SelectionCount = 0;
        }

        /// <summary>Set all canvas slots as multi-selected (Ctrl+A).</summary>
        public void SelectAllMulti()
        {
            ClearMultiSelect();
            foreach (var s in CanvasSlots)
            {
                MultiSelectedSlots.Add(s);
                s.IsMultiSelected = true;
            }
            SelectionCount = MultiSelectedSlots.Count;
            if (CanvasSlots.Count > 0)
                SelectedCanvasSlot = CanvasSlots.Last();
        }

        // ── Copy / Paste / Duplicate ──────────────────────────────────────

        [RelayCommand]
        private void CopySlots()
        {
            var sources = MultiSelectedSlots.Count > 0
                ? MultiSelectedSlots
                : (SelectedCanvasSlot != null ? new List<CanvasSlotItem> { SelectedCanvasSlot } : new());
            if (sources.Count == 0) return;

            _clipboardSlots = sources
                .Select(s => System.Text.Json.JsonSerializer.Deserialize<TemplateSlotDefinition>(
                    System.Text.Json.JsonSerializer.Serialize(s.Slot, _undoJsonOpts), _undoJsonOpts)!)
                .ToList();
            OnPropertyChanged(nameof(HasClipboard));
            SetHint(Lf("Des_Copied", _clipboardSlots.Count));
        }

        [RelayCommand]
        private void PasteSlots()
        {
            if (_clipboardSlots == null || _clipboardSlots.Count == 0 || CurrentPage == null) return;
            PushUndo();
            CurrentPage.Slots ??= new List<TemplateSlotDefinition>();
            const double offsetMm = 5.0;
            foreach (var original in _clipboardSlots)
            {
                var json = System.Text.Json.JsonSerializer.Serialize(original, _undoJsonOpts);
                var copy = System.Text.Json.JsonSerializer.Deserialize<TemplateSlotDefinition>(json, _undoJsonOpts)!;
                copy.Id = Guid.NewGuid().ToString();
                copy.Name = (copy.Name ?? L("Des_Field")) + L("Des_CopyParen");
                copy.X += offsetMm;
                copy.Y += offsetMm;
                CurrentPage.Slots.Add(copy);
            }
            RebuildCanvasSlots();
            IsDirty = true;
            SetSuccess(Lf("Des_Pasted", _clipboardSlots.Count));
        }

        [RelayCommand]
        private void DuplicateSlots()
        {
            CopySlots();
            PasteSlots();
        }

        // ── Alignment commands (multi-select) ─────────────────────────────

        [RelayCommand]
        private void AlignSlotsLeft()
        {
            if (MultiSelectedSlots.Count < 2) return;
            PushUndo();
            double min = MultiSelectedSlots.Min(s => s.Slot.X);
            foreach (var s in MultiSelectedSlots) { s.Slot.X = min; s.RefreshFromSlot(); }
            IsDirty = true;
            SetHint(L("Des_AlignLeft"));
        }

        [RelayCommand]
        private void AlignSlotsRight()
        {
            if (MultiSelectedSlots.Count < 2) return;
            PushUndo();
            double max = MultiSelectedSlots.Max(s => s.Slot.X + s.Slot.Width);
            foreach (var s in MultiSelectedSlots) { s.Slot.X = max - s.Slot.Width; s.RefreshFromSlot(); }
            IsDirty = true;
            SetHint(L("Des_AlignRight"));
        }

        [RelayCommand]
        private void AlignSlotsTop()
        {
            if (MultiSelectedSlots.Count < 2) return;
            PushUndo();
            double min = MultiSelectedSlots.Min(s => s.Slot.Y);
            foreach (var s in MultiSelectedSlots) { s.Slot.Y = min; s.RefreshFromSlot(); }
            IsDirty = true;
            SetHint(L("Des_AlignTop"));
        }

        [RelayCommand]
        private void AlignSlotsBottom()
        {
            if (MultiSelectedSlots.Count < 2) return;
            PushUndo();
            double max = MultiSelectedSlots.Max(s => s.Slot.Y + s.Slot.Height);
            foreach (var s in MultiSelectedSlots) { s.Slot.Y = max - s.Slot.Height; s.RefreshFromSlot(); }
            IsDirty = true;
            SetHint(L("Des_AlignBottom"));
        }

        [RelayCommand]
        private void AlignSlotsCenterH()
        {
            if (MultiSelectedSlots.Count < 2) return;
            PushUndo();
            double avgCx = MultiSelectedSlots.Average(s => s.Slot.X + s.Slot.Width / 2);
            foreach (var s in MultiSelectedSlots) { s.Slot.X = avgCx - s.Slot.Width / 2; s.RefreshFromSlot(); }
            IsDirty = true;
            SetHint(L("Des_CenterH"));
        }

        [RelayCommand]
        private void AlignSlotsCenterV()
        {
            if (MultiSelectedSlots.Count < 2) return;
            PushUndo();
            double avgCy = MultiSelectedSlots.Average(s => s.Slot.Y + s.Slot.Height / 2);
            foreach (var s in MultiSelectedSlots) { s.Slot.Y = avgCy - s.Slot.Height / 2; s.RefreshFromSlot(); }
            IsDirty = true;
            SetHint(L("Des_CenterV"));
        }

        [RelayCommand]
        private void DistributeSlotsH()
        {
            if (MultiSelectedSlots.Count < 3) return;
            PushUndo();
            var sorted = MultiSelectedSlots.OrderBy(s => s.Slot.X).ToList();
            double first = sorted.First().Slot.X;
            double last = sorted.Last().Slot.X;
            double step = (last - first) / (sorted.Count - 1);
            for (int i = 1; i < sorted.Count - 1; i++)
            {
                sorted[i].Slot.X = first + step * i;
                sorted[i].RefreshFromSlot();
            }
            IsDirty = true;
            SetHint(L("Des_DistributeH"));
        }

        [RelayCommand]
        private void DistributeSlotsV()
        {
            if (MultiSelectedSlots.Count < 3) return;
            PushUndo();
            var sorted = MultiSelectedSlots.OrderBy(s => s.Slot.Y).ToList();
            double first = sorted.First().Slot.Y;
            double last = sorted.Last().Slot.Y;
            double step = (last - first) / (sorted.Count - 1);
            for (int i = 1; i < sorted.Count - 1; i++)
            {
                sorted[i].Slot.Y = first + step * i;
                sorted[i].RefreshFromSlot();
            }
            IsDirty = true;
            SetHint(L("Des_DistributeV"));
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
                bmp.CacheOption = BitmapCacheOption.OnLoad;
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
                    : string.IsNullOrEmpty(SelectedCategory) || SelectedCategory == L("Des_All")
                        ? _library.GetAll()
                        : _library.GetByCategory(SelectedCategory);

            Templates.Clear();
            foreach (var e in base_) Templates.Add(e);
        }

        private DispatcherTimer? _statusTimer;

        private void SetSuccess(string msg) { StatusMessage = msg; StatusColor = "#22C55E"; ArmStatusTimer(); }
        private void SetError(string msg)   { StatusMessage = msg; StatusColor = "#EF4444"; ArmStatusTimer(); }

        private void ArmStatusTimer()
        {
            _statusTimer?.Stop();
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _statusTimer.Tick += (_, _) => { StatusMessage = ""; _statusTimer?.Stop(); };
            _statusTimer.Start();
        }

        /// <summary>Show a transient info hint (blue colour, same auto-clear timer).</summary>
        public void SetHint(string msg) { StatusMessage = msg; StatusColor = "#3B82F6"; ArmStatusTimer(); }

        /// <summary>Called by the drag handler after each completed drag movement.</summary>
        public void NotifyDragDirty() => IsDirty = true;

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
        public bool IsSmartStep => ActiveStep > 0;

        // ── Binding section: column bound to currently selected canvas slot ───
        [ObservableProperty] private string? _selectedSlotBoundColumn;

        // Constructor — wires Session into SmartVars
        public TemplateDesignerViewModel()
        {
            Session = new TemplateDesignerSession();
            SmartVars = new SmartVariablesViewModel();
            SmartVars.AttachSession(Session);

            // Re-sync the rendering context whenever the user switches between
            // Smart Variables tabs (Paste / Map / Images / Preview / Export).
            // This guarantees _session.CurrentPage is always populated when
            // RefreshPreview() runs, even if the user skipped the stepper buttons.
            SmartVars.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SmartVariablesViewModel.ActiveTabIndex)
                    && HasTemplate)
                    SyncFieldsToSession();
            };
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
                    FieldId = SelectedSlot.Id,
                    FieldLabel = !string.IsNullOrWhiteSpace(SelectedSlot.Name)
                                    ? SelectedSlot.Name
                                    : (SelectedSlot.VariableName ?? ""),
                    FieldType = MapSlotType(SelectedSlot.DataType),
                    IsRequired = SelectedSlot.Required,
                    ColumnName = value,
                    Confidence = MappingConfidence.High,
                });
            }

            // Also propagate into SmartVars so mapping tab stays in sync
            SmartVars.SetMapping(SelectedSlot.Id, value);
        }

        // ── Step navigation command ───────────────────────────────────────────

        /// <summary>
        /// Navigate to a workflow step.
        /// Accepts the step as a STRING so that XAML CommandParameter="0" (which is always
        /// a string in WPF) is correctly received by RelayCommand&lt;string&gt;.
        /// CommunityToolkit.Mvvm 8.x does not TypeConvert the CommandParameter value, so a
        /// RelayCommand&lt;int&gt; would silently never execute when given a string "0".
        /// </summary>
        [RelayCommand]
        private void GoToStep(string? stepStr)
        {
            if (!int.TryParse(stepStr, out int step)) return;
            // Step 0 (Design) is always accessible.
            // Steps 1-5 require a loaded template; the session also gates each step.
            if (step > 0 && !HasTemplate) return;
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

            PushUndo();
            CurrentPage.Slots ??= new List<TemplateSlotDefinition>();
            int n = CurrentPage.Slots.Count + 1;

            string label = dataType switch
            {
                SlotDataType.Text => Lf("Des_TextN", n),
                SlotDataType.Number => Lf("Des_NumberN", n),
                SlotDataType.Date => Lf("Des_DateN", n),
                SlotDataType.Image => Lf("Des_ImageN", n),
                SlotDataType.QrCode => $"QR {n}",
                SlotDataType.Barcode => Lf("Des_BarcodeN", n),
                SlotDataType.Counter => Lf("Des_CounterN", n),
                _ => Lf("Des_FieldN", n),
            };

            var slot = new TemplateSlotDefinition
            {
                Name = label,
                VariableName = $"var{n}",
                X = 10,
                Y = 10 + (n - 1) * 18,
                Width = 80,
                Height = 15,
                FontFamily = "Tahoma",
                FontSize = 12,
                DataType = dataType,
                IsRtl = true,
                TextAlign = HorizontalAlign.Right,
                TextColor = "#000000",
            };

            CurrentPage.Slots.Add(slot);
            RebuildCanvasSlots();
            SelectedCanvasSlot = CanvasSlots.LastOrDefault();
            IsDirty = true;
            SetSuccess(Lf("Des_Added", label));
        }

        // ── Open / close (for legacy button + keyboard shortcut) ─────────────

        [RelayCommand]
        private void OpenSmartPanel() => GoToStep("1");

        [RelayCommand]
        private void CloseSmartPanel() => ActiveStep = 0;

        /// <summary>
        /// Called by the drag handler in code-behind after each mouse-move update.
        /// Keeps the right-panel X/Y text boxes in sync while dragging without
        /// re-loading all slot fields (which would reset focus / scroll).
        /// </summary>
        public void OnSlotDragPositionChanged(CanvasSlotItem item)
        {
            if (SelectedSlot == item.Slot)
            {
                SlotX = Math.Round(item.Slot.X, 1);
                SlotY = Math.Round(item.Slot.Y, 1);
            }
        }

        /// <summary>
        /// Called by the resize handler in code-behind after each mouse-move update.
        /// Keeps the right-panel W/H text boxes in sync while resizing.
        /// </summary>
        public void OnSlotResizeChanged(CanvasSlotItem item)
        {
            if (SelectedSlot == item.Slot)
            {
                SlotW = Math.Round(item.Slot.Width, 1);
                SlotH = Math.Round(item.Slot.Height, 1);
            }
        }

        // ── Save with Smart Variables data ────────────────────────────────────

        [RelayCommand]
        private void SaveWithSmartData()
        {
            if (CurrentTemplate == null || string.IsNullOrWhiteSpace(SelectedEntry?.FilePath)) return;
            try
            {
                var state = SmartVars.SnapshotState();
                ApextFileFormat.SaveWithSmartData(CurrentTemplate, state, SelectedEntry.FilePath, _currentAssets);
                SetSuccess(L("Des_SavedWithVars"));
            }
            catch (Exception ex)
            {
                SetError(Lf("Des_SaveError", ex.Message));
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
                Id = s.Id,
                Label = !string.IsNullOrWhiteSpace(s.Name) ? s.Name : (s.VariableName ?? L("Des_Field")),
                VariableKey = s.VariableName ?? "",
                FieldType = MapSlotType(s.DataType),
                IsRequired = s.Required,
            }).ToList();

            Session.SyncFields(fields);

            // ── v2.0.9 FIX: SyncRenderingContext BEFORE SetTemplateFields ────────
            // SetTemplateFields triggers auto-map → OnMappedCountChanged → RefreshPreview().
            // RefreshPreview() needs _session.CurrentPage to be non-null, otherwise it
            // returns a null RenderedTemplate and the canvas stays blank / shows
            // [غير مربوط].  Setting the rendering context first guarantees CurrentPage
            // is always populated by the time RefreshPreview() is called from within
            // SetTemplateFields (or from OnActiveTabIndexChanged immediately after).
            Session.SyncRenderingContext(CurrentPage, _currentAssets);

            SmartVars.SetTemplateFields(fields);

            // Explicit refresh: if the user is already on the Preview or Export tab
            // and MappedCount didn't change (so OnMappedCountChanged didn't fire),
            // we still need to re-render with the freshly synced CurrentPage.
            if (SmartVars.ActiveTabIndex == 3 || SmartVars.ActiveTabIndex == 4)
                SmartVars.ForceRefreshPreview();
        }

        private static SmartFieldType MapSlotType(SlotDataType dt) => dt switch
        {
            SlotDataType.Text => SmartFieldType.TextVariable,
            SlotDataType.Number => SmartFieldType.NumberVariable,
            SlotDataType.Date => SmartFieldType.DateVariable,
            SlotDataType.Image => SmartFieldType.ImageVariable,
            SlotDataType.Barcode => SmartFieldType.Barcode,
            SlotDataType.QrCode => SmartFieldType.QRCode,
            SlotDataType.Counter => SmartFieldType.AutoSerial,
            _ => SmartFieldType.TextVariable,
        };
    }
}
