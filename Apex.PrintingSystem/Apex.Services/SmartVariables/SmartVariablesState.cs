using System;
using System.Collections.Generic;
using Apex.Services.SmartVariables.Models;

namespace Apex.Services.SmartVariables
{
    /// <summary>
    /// Complete, serializable project state for the Smart Variables system.
    /// Serialized as "smartdata.json" inside the .apext ZIP archive.
    /// </summary>
    public class SmartVariablesState
    {
        public string Version { get; set; } = "1.0";
        public DateTime LastSaved { get; set; } = DateTime.Now;

        // ── Data source ────────────────────────────────────────────────────────
        public SmartDataSource DataSource { get; set; } = new();

        // ── Field → column bindings ────────────────────────────────────────────
        public List<VariableMapping> Mappings { get; set; } = new();

        // ── Image matching configuration ───────────────────────────────────────
        public string ImageFolder { get; set; } = "";
        public ImageMatchMode ImageMatchMode { get; set; } = ImageMatchMode.ByFileName;
        public string ImageKeyColumn { get; set; } = "";
        public string StaticImagePath { get; set; } = "";

        // ── Export settings ────────────────────────────────────────────────────
        public ExportSettings ExportSettings { get; set; } = new();

        // ── UI state (restored on reopen) ──────────────────────────────────────
        public int PreviewRowIndex { get; set; } = 0;
        public int ActiveTabIndex { get; set; } = 0;
        public bool ShowPreviewOverlay { get; set; }

        // ── Helpers ────────────────────────────────────────────────────────────

        /// <summary>True when the state has meaningful data to save.</summary>
        public bool HasContent =>
            DataSource.HasData || Mappings.Count > 0 || !string.IsNullOrEmpty(ImageFolder);

        /// <summary>Resets to a clean initial state (new project).</summary>
        public void Reset()
        {
            DataSource = new SmartDataSource();
            Mappings = new List<VariableMapping>();
            ImageFolder = "";
            ImageMatchMode = ImageMatchMode.ByFileName;
            ImageKeyColumn = "";
            StaticImagePath = "";
            ExportSettings = new ExportSettings();
            PreviewRowIndex = 0;
            ActiveTabIndex = 0;
            ShowPreviewOverlay = false;
        }
    }
}
