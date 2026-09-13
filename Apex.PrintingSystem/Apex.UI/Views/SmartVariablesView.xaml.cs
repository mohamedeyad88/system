using Apex.UI.ViewModels;
using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Apex.UI.Views
{
    public partial class SmartVariablesView : UserControl
    {
        public SmartVariablesView()
        {
            InitializeComponent();
            Loaded += SmartVariablesView_Loaded;
        }

        // DataContext is SmartVariablesViewModel (set by parent TemplateDesignerView)
        private SmartVariablesViewModel? VM => DataContext as SmartVariablesViewModel;

        // ── Loaded ────────────────────────────────────────────────────────────
        // Wire the Export PNG button here (code-behind, not VM) because it needs
        // direct access to the TemplatePreviewRoot visual element.

        private void SmartVariablesView_Loaded(object sender, RoutedEventArgs e)
        {
            if (BtnExportPng != null)
                BtnExportPng.Click += BtnExportPng_Click;
        }

        // ── PNG Export ────────────────────────────────────────────────────────

        private void BtnExportPng_Click(object sender, RoutedEventArgs e)
        {
            var vm = VM;
            if (vm == null) return;

            if (vm.CurrentRenderedTemplate == null)
            {
                vm.ExportPreviewStatus = "⚠ لا توجد معاينة — اربط المتغيرات أولاً";
                vm.ExportPreviewStatusColor = StatusPalette.Warning;
                return;
            }

            // Build a default filename from the current record
            string defaultName = BuildDefaultFileName(vm);

            var dlg = new SaveFileDialog
            {
                Title = "حفظ السجل الحالي كـ PNG",
                Filter = "صورة PNG|*.png",
                FileName = defaultName,
                AddExtension = true,
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                // TemplatePreviewRoot is the inner Grid (background + fields).
                // Rendering it directly produces a pixel-perfect copy of what
                // the user sees — same renderer, same visual tree.
                ExportElementToPng(TemplatePreviewRoot, dlg.FileName, dpi: 150);

                string shortName = Path.GetFileName(dlg.FileName);
                vm.ExportPreviewStatus = $"✅ تم الحفظ: {shortName}";
                vm.ExportPreviewStatusColor = StatusPalette.Done;
            }
            catch (Exception ex)
            {
                vm.ExportPreviewStatus = $"خطأ في التصدير: {ex.Message}";
                vm.ExportPreviewStatusColor = StatusPalette.Error;
                MessageBox.Show(
                    $"فشل تصدير PNG:\n{ex.Message}",
                    "خطأ في التصدير",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string BuildDefaultFileName(SmartVariablesViewModel vm)
        {
            string idx = (vm.PreviewRowIndex + 1).ToString("D4");

            // Try to find a "name" field for a more descriptive filename
            var nameField = vm.PreviewFields.FirstOrDefault(f =>
                f.FieldLabel.Contains("اسم", StringComparison.OrdinalIgnoreCase) ||
                f.FieldLabel.Contains("name", StringComparison.OrdinalIgnoreCase));

            if (nameField != null && !string.IsNullOrWhiteSpace(nameField.Value))
            {
                // Sanitise
                string safe = string.Concat(nameField.Value
                    .Where(c => c != '/' && c != '\\' && c != ':' &&
                                c != '*' && c != '?' && c != '"' &&
                                c != '<' && c != '>' && c != '|'));
                return $"{idx}_{safe}";
            }

            return $"record_{idx}";
        }

        /// <summary>
        /// Renders any WPF <see cref="FrameworkElement"/> to a PNG file at the
        /// given DPI.  The element is rendered at its natural (un-scaled) size ×
        /// the DPI scale factor, producing a high-resolution image identical to
        /// the live preview.
        /// </summary>
        private static void ExportElementToPng(
            FrameworkElement element,
            string filePath,
            int dpi = 150)
        {
            // Force layout so ActualWidth / ActualHeight are valid even if the
            // element has not yet been displayed at full size.
            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            element.Arrange(new Rect(element.DesiredSize));
            element.UpdateLayout();

            double scale = dpi / 96.0;
            int width = Math.Max(1, (int)(element.ActualWidth * scale));
            int height = Math.Max(1, (int)(element.ActualHeight * scale));

            var rtb = new RenderTargetBitmap(
                width, height,
                dpi, dpi,
                PixelFormats.Pbgra32);

            // Render the element directly: RenderTargetBitmap already scales DIPs by
            // dpi/96. (Wrapping it in a VisualBrush{Stretch=None} centred the content
            // inside the larger bitmap and clipped its right/bottom edges.)
            rtb.Render(element);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));

            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using var stream = File.Create(filePath);
            encoder.Save(stream);
        }
    }
}
