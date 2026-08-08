namespace Apex.Services.SmartVariables.Models
{
    public enum ExportScope { AllValid, CurrentRecord, SelectedRecords }
    public enum ExportFormat { SinglePdf, SeparatePdfs, Png, Jpg, DirectPrint }
    public enum FileNamingMode { BySerial, ByCode, ByName, ByFormula }
    public enum PageLayout { SingleItem, MultipleItems }

    public class ExportSettings
    {
        public ExportScope Scope { get; set; } = ExportScope.AllValid;
        public ExportFormat Format { get; set; } = ExportFormat.SinglePdf;
        public string OutputFolder { get; set; } = "";

        // File naming
        public FileNamingMode NamingMode { get; set; } = FileNamingMode.BySerial;
        public string NamingColumn { get; set; } = "";
        public string NamingFormula { get; set; } = "{{id}}-{{name}}";

        // Multi-item page layout
        public PageLayout Layout { get; set; } = PageLayout.SingleItem;
        public int GridColumns { get; set; } = 2;
        public int GridRows { get; set; } = 5;
        public double SpacingHMm { get; set; } = 3;
        public double SpacingVMm { get; set; } = 3;
        public double MarginTopMm { get; set; } = 10;
        public double MarginBottomMm { get; set; } = 10;
        public double MarginLeftMm { get; set; } = 10;
        public double MarginRightMm { get; set; } = 10;
        public bool ShowCutMarks { get; set; }

        // Quality
        public int DpiResolution { get; set; } = 300;
        public bool IgnoreWarnings { get; set; }
        public bool ExportOnlyValid { get; set; } = true;

        // Printer (for DirectPrint)
        public string PrinterName { get; set; } = "";
        public int PrintCopies { get; set; } = 1;
    }
}
