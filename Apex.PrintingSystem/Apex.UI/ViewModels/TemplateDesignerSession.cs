using CommunityToolkit.Mvvm.ComponentModel;
using Apex.Services.SmartVariables;
using Apex.Services.SmartVariables.Models;
using Apex.Services.Templates;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace Apex.UI.ViewModels
{
    /// <summary>
    /// Single shared state for all 6 workflow steps of the Template Designer.
    /// Owned by TemplateDesignerViewModel and passed by reference to SmartVariablesViewModel.
    /// This eliminates the disconnect between the Canvas and the Smart Variables workflow.
    /// </summary>
    public partial class TemplateDesignerSession : ObservableObject
    {
        // ── Step metadata ─────────────────────────────────────────────────────
        public static readonly (string Icon, string Label, string ShortLabel)[] Steps =
        {
            ("📄", "تصميم القالب",   "تصميم"),
            ("📋", "لصق البيانات",   "لصق"),
            ("🔗", "ربط المتغيرات",  "ربط"),
            ("🖼", "إدارة الصور",    "صور"),
            ("👁", "معاينة",         "معاينة"),
            ("✅", "فحص وتصدير",    "تصدير"),
        };

        // ── Canvas fields (synced from slots every time user enters a Smart step) ──
        private List<SmartTemplateField> _fields = new();
        public IReadOnlyList<SmartTemplateField> Fields => _fields;

        // ── Data (Step 1 — Paste) ─────────────────────────────────────────────
        public SmartDataSource DataSource { get; set; } = new();

        /// <summary>
        /// DataView built from DataSource — bound to the DataGrid in the Paste step.
        /// Exposes REAL column names (الاسم, الصف, …) instead of model properties.
        /// </summary>
        [ObservableProperty] private DataView? _dataTableView;

        public bool HasData => DataSource.HasData;
        public List<string> DataColumns => DataSource.Columns;

        // ── Mappings (Step 2 — Map) ───────────────────────────────────────────
        public List<VariableMapping> Mappings { get; set; } = new();

        // ── Images (Step 3) ───────────────────────────────────────────────────
        [ObservableProperty] private string _imageFolder = "";
        [ObservableProperty] private ImageMatchMode _imageMatchMode = ImageMatchMode.ByFileName;
        [ObservableProperty] private string _imageKeyColumn = "";
        [ObservableProperty] private string _staticImagePath = "";
        public List<ImageAsset> ImageLibrary { get; set; } = new();

        // ── Preview (Step 4) ──────────────────────────────────────────────────
        [ObservableProperty] private int _previewRecordIndex = 0;

        // ── Export (Step 5) ───────────────────────────────────────────────────
        public ExportSettings ExportSettings { get; set; } = new();

        // ── Rendering context (set by TemplateDesignerViewModel) ─────────────
        /// <summary>
        /// The currently loaded template page (slots + background).
        /// Set by <see cref="TemplateDesignerViewModel.SyncFieldsToSession"/> whenever
        /// the user enters a Smart Variables step.  Used by TemplateRenderingService
        /// to render the live preview and export PNG.
        /// </summary>
        public TemplatePageDefinition? CurrentPage { get; set; }

        /// <summary>
        /// Binary assets (background images, static image slots) from the loaded .apext file.
        /// Keyed by asset filename (case-insensitive).
        /// </summary>
        public Dictionary<string, byte[]> Assets { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);

        // ── Computed ──────────────────────────────────────────────────────────

        public bool CanMap => HasData && _fields.Count > 0;
        public bool NeedsImages => _fields.Any(f => f.FieldType == SmartFieldType.ImageVariable);

        /// <summary>
        /// Resolved display value for a specific field at the current preview record.
        /// Returns "" when not mapped or no data.
        /// </summary>
        public string GetPreviewValue(string fieldId)
        {
            if (!HasData || DataSource.Rows.Count == 0) return "";
            int idx = Math.Clamp(PreviewRecordIndex, 0, DataSource.Rows.Count - 1);
            var row = DataSource.Rows[idx];
            var mapping = Mappings.FirstOrDefault(m => m.FieldId == fieldId && m.IsMapped);
            if (mapping == null) return "";
            return row.Get(mapping.ColumnName!, "");
        }

        // ── Mutation helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Replace the data source and rebuild the DataGrid view.
        /// Called by SmartVariablesViewModel after a paste/clean operation.
        /// </summary>
        public void SetDataSource(SmartDataSource src)
        {
            DataSource = src;
            RebuildDataTableView();
            OnPropertyChanged(nameof(HasData));
            OnPropertyChanged(nameof(DataColumns));
            OnPropertyChanged(nameof(CanMap));
        }

        /// <summary>
        /// Replace mappings list.  Called after auto-map or manual mapping changes.
        /// </summary>
        public void SetMappings(List<VariableMapping> mappings)
        {
            Mappings = mappings;
            OnPropertyChanged(nameof(CanMap));
        }

        /// <summary>
        /// Push the current page definition and asset bytes from the loaded template
        /// into the session so <see cref="Services.TemplateRenderingService"/> has
        /// everything it needs to render the live preview and export PNG.
        /// Called by <see cref="TemplateDesignerViewModel.SyncFieldsToSession"/>.
        /// </summary>
        public void SyncRenderingContext(
            TemplatePageDefinition? page,
            Dictionary<string, byte[]> assets)
        {
            CurrentPage = page;
            Assets = new Dictionary<string, byte[]>(assets, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Sync canvas slots → SmartTemplateField list.
        /// Drops mappings for fields that no longer exist.
        /// </summary>
        public void SyncFields(IEnumerable<SmartTemplateField> incoming)
        {
            var list = incoming.ToList();
            // Preserve mappings whose field ID still exists
            Mappings = Mappings
                .Where(m => list.Any(f => f.Id == m.FieldId))
                .ToList();
            _fields = list;
            OnPropertyChanged(nameof(Fields));
            OnPropertyChanged(nameof(CanMap));
            OnPropertyChanged(nameof(NeedsImages));
        }

        /// <summary>
        /// (Re)build the DataTable view that the DataGrid binds to.
        /// First column is ● (status icon), remaining columns are the real pasted headers.
        /// </summary>
        public void RebuildDataTableView()
        {
            if (!DataSource.HasData)
            {
                DataTableView = null;
                return;
            }

            var dt = new DataTable();
            dt.Columns.Add("●", typeof(string));
            foreach (var col in DataSource.Columns)
                dt.Columns.Add(col, typeof(string));

            foreach (var row in DataSource.Rows)
            {
                var dr = dt.NewRow();
                dr["●"] = row.StatusIcon;
                foreach (var col in DataSource.Columns)
                    dr[col] = row.Get(col);
                dt.Rows.Add(dr);
            }

            DataTableView = dt.DefaultView;
        }

        /// <summary>Reset all Smart Variables data (keeps template fields).</summary>
        public void ResetSmartData()
        {
            DataSource = new SmartDataSource();
            DataTableView = null;
            Mappings = new List<VariableMapping>();
            ImageLibrary = new List<ImageAsset>();
            ImageFolder = "";
            PreviewRecordIndex = 0;
            OnPropertyChanged(nameof(HasData));
            OnPropertyChanged(nameof(DataColumns));
            OnPropertyChanged(nameof(CanMap));
        }

        /// <summary>Whether a given workflow step is accessible (used to disable stepper buttons).</summary>
        public bool IsStepEnabled(int step) => step switch
        {
            0 => true,
            1 => true,
            2 => HasData && _fields.Count > 0,
            3 => HasData,
            4 => HasData && Mappings.Any(m => m.IsMapped),
            5 => HasData && Mappings.Any(m => m.IsMapped),
            _ => false
        };

        // ── Persistence helpers ───────────────────────────────────────────────

        /// <summary>Serialize to SmartVariablesState for saving inside the .apext ZIP.</summary>
        public SmartVariablesState ToSmartVariablesState(int activeStep) => new()
        {
            Version = "1.0",
            LastSaved = DateTime.Now,
            DataSource = DataSource,
            Mappings = Mappings,
            ImageFolder = ImageFolder,
            ImageMatchMode = ImageMatchMode,
            ImageKeyColumn = ImageKeyColumn,
            StaticImagePath = StaticImagePath,
            ExportSettings = ExportSettings,
            PreviewRowIndex = PreviewRecordIndex,
            ActiveTabIndex = activeStep,
        };

        /// <summary>Restore from a previously persisted SmartVariablesState.</summary>
        public void LoadFromState(SmartVariablesState state)
        {
            DataSource = state.DataSource;
            Mappings = state.Mappings;
            ImageFolder = state.ImageFolder;
            ImageMatchMode = state.ImageMatchMode;
            ImageKeyColumn = state.ImageKeyColumn;
            StaticImagePath = state.StaticImagePath;
            ExportSettings = state.ExportSettings;
            PreviewRecordIndex = state.PreviewRowIndex;
            RebuildDataTableView();
            OnPropertyChanged(nameof(HasData));
            OnPropertyChanged(nameof(DataColumns));
            OnPropertyChanged(nameof(CanMap));
        }
    }
}
