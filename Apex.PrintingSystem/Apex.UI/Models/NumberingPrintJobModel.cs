using Apex.NumberedBooksEngine.Models;
using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.Linq;

namespace Apex.UI.Models
{
    /// <summary>
    /// Single Source of Truth for numbering print job configuration.
    /// All calculations are performed ONCE and stored here.
    /// UI screens read from this model, never recalculate.
    /// </summary>
    public class NumberingPrintJobModel
    {
        // ═══════════════════════════════════════════════════════════════════
        // INPUT PROPERTIES (set by user)
        // ═══════════════════════════════════════════════════════════════════
        public string? PrinterName { get; set; }
        public string? TemplatePath { get; set; }
        public long StartNumber { get; set; } = 1;
        public long TotalNumbers { get; set; } = 100;
        public int NumberOfCopies { get; set; } = 1; // 1-3 (Original + copies)
        public NumberingMode NumberingMode { get; set; } = NumberingMode.Linear;
        public List<NumberSlot> Slots { get; set; } = new();
        
        // Tray mappings (raw values from printer)
        public string? OriginalTray { get; set; }
        public string? Copy1Tray { get; set; }
        public string? Copy2Tray { get; set; }
        
        // ═══════════════════════════════════════════════════════════════════
        // COMPUTED PROPERTIES (calculated ONCE, never recalculated)
        // ═══════════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Total number of copies (Original + Copy1 + Copy2)
        /// Always: NumberOfCopies (1-3)
        /// </summary>
        public int CopiesCount => NumberOfCopies;
        
        /// <summary>
        /// Whether multiple copies are enabled
        /// </summary>
        public bool HasMultipleCopies => CopiesCount > 1;
        
        /// <summary>
        /// Total pages needed for printing.
        /// Calculation depends on numbering mode:
        /// - Linear: (TotalNumbers / SlotsPerPage) * CopiesCount
        /// - Cutting: (TotalNumbers / SlotsPerPage) * CopiesCount
        /// </summary>
        public long TotalPages
        {
            get
            {
                if (Slots == null || Slots.Count == 0)
                    return 0;
                
                int slotsPerPage = Slots.Count;
                long pagesPerCopy = (long)Math.Ceiling((double)TotalNumbers / slotsPerPage);
                return pagesPerCopy * CopiesCount;
            }
        }
        
        /// <summary>
        /// Pages per copy (before multiplying by copies)
        /// </summary>
        public long PagesPerCopy
        {
            get
            {
                if (Slots == null || Slots.Count == 0)
                    return 0;
                
                int slotsPerPage = Slots.Count;
                return (long)Math.Ceiling((double)TotalNumbers / slotsPerPage);
            }
        }
        
        /// <summary>
        /// Slots per page
        /// </summary>
        public int SlotsPerPage => Slots?.Count ?? 0;
        
        /// <summary>
        /// End number (StartNumber + TotalNumbers - 1)
        /// </summary>
        public long EndNumber => StartNumber + TotalNumbers - 1;
        
        /// <summary>
        /// Whether job is valid and ready to print
        /// </summary>
        public bool IsValid
        {
            get
            {
                return !string.IsNullOrEmpty(PrinterName) &&
                       !string.IsNullOrEmpty(TemplatePath) &&
                       Slots != null &&
                       Slots.Count > 0 &&
                       TotalNumbers > 0 &&
                       NumberOfCopies >= 1 &&
                       NumberOfCopies <= 3;
            }
        }
        
        /// <summary>
        /// Validation errors (if any)
        /// </summary>
        public List<string> ValidationErrors
        {
            get
            {
                var errors = new List<string>();
                
                if (string.IsNullOrEmpty(PrinterName))
                    errors.Add("يرجى اختيار طابعة");
                
                if (string.IsNullOrEmpty(TemplatePath))
                    errors.Add("يرجى تحديد مسار القالب");
                
                if (Slots == null || Slots.Count == 0)
                    errors.Add("يرجى إضافة slot واحد على الأقل");
                
                if (TotalNumbers <= 0)
                    errors.Add("إجمالي الأرقام يجب أن يكون أكبر من صفر");
                
                if (NumberOfCopies < 1 || NumberOfCopies > 3)
                    errors.Add("عدد النسخ يجب أن يكون بين 1 و 3");
                
                return errors;
            }
        }
        
        // ═══════════════════════════════════════════════════════════════════
        // CALCULATION METHODS (called ONCE when model is updated)
        // ═══════════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Calculates preview number for a slot based on numbering mode.
        /// This is the ONLY place where preview numbers are calculated.
        /// </summary>
        public string CalculatePreviewNumber(int slotIndex)
        {
            if (Slots == null || slotIndex < 0 || slotIndex >= Slots.Count)
                return "0000";
            
            long previewNum;
            
            if (NumberingMode == NumberingMode.Linear)
            {
                // Linear mode: sequential numbers (1, 2, 3, 4...)
                // Page 1: [1, 2, 3, 4]
                previewNum = StartNumber + slotIndex;
            }
            else if (NumberingMode == NumberingMode.Imposed)
            {
                // Cutting mode: calculate based on formula
                // Formula: value = start + pageIndex + (slotIndex * totalPages)
                // For preview (first page), pageIndex = 0
                long totalPages = PagesPerCopy;
                long pageIndex = 0; // Preview shows first page
                previewNum = StartNumber + pageIndex + (slotIndex * totalPages);
            }
            else
            {
                // Default: sequential
                previewNum = StartNumber + slotIndex;
            }
            
            return previewNum.ToString("D4");
        }
        
        /// <summary>
        /// Updates preview numbers for all slots.
        /// Call this when StartNumber, TotalNumbers, or NumberingMode changes.
        /// </summary>
        public void UpdatePreviewNumbers()
        {
            if (Slots == null) return;
            
            for (int i = 0; i < Slots.Count; i++)
            {
                Slots[i].PreviewNumber = CalculatePreviewNumber(i);
            }
        }
        
        /// <summary>
        /// Builds tray mapping dictionary for print service.
        /// </summary>
        public Dictionary<int, PaperSourceKind> BuildTrayMapping()
        {
            var mapping = new Dictionary<int, PaperSourceKind>();
            
            // Original (Copy 0)
            if (!string.IsNullOrEmpty(OriginalTray) &&
                Enum.TryParse<PaperSourceKind>(OriginalTray, out var originalTray))
            {
                mapping[0] = originalTray;
            }
            else
            {
                mapping[0] = PaperSourceKind.Upper; // Default
            }
            
            // Copy 1
            if (NumberOfCopies >= 2 && 
                !string.IsNullOrEmpty(Copy1Tray) &&
                Enum.TryParse<PaperSourceKind>(Copy1Tray, out var copy1Tray))
            {
                mapping[1] = copy1Tray;
            }
            
            // Copy 2
            if (NumberOfCopies >= 3 && 
                !string.IsNullOrEmpty(Copy2Tray) &&
                Enum.TryParse<PaperSourceKind>(Copy2Tray, out var copy2Tray))
            {
                mapping[2] = copy2Tray;
            }
            
            return mapping;
        }
        
        /// <summary>
        /// Creates a copy of this model (for navigation/undo)
        /// </summary>
        public NumberingPrintJobModel Clone()
        {
            return new NumberingPrintJobModel
            {
                PrinterName = PrinterName,
                TemplatePath = TemplatePath,
                StartNumber = StartNumber,
                TotalNumbers = TotalNumbers,
                NumberOfCopies = NumberOfCopies,
                NumberingMode = NumberingMode,
                Slots = Slots?.Select(s => new NumberSlot
                {
                    X = s.X,
                    Y = s.Y,
                    Width = s.Width,
                    Height = s.Height,
                    FontFamily = s.FontFamily,
                    FontSize = s.FontSize,
                    FontColor = s.FontColor,
                    IsBold = s.IsBold,
                    Rotation = s.Rotation,
                    Opacity = s.Opacity,
                    PreviewNumber = s.PreviewNumber
                }).ToList() ?? new List<NumberSlot>(),
                OriginalTray = OriginalTray,
                Copy1Tray = Copy1Tray,
                Copy2Tray = Copy2Tray
            };
        }
    }
}


