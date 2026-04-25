using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Apex.UI.ViewModels;

namespace Apex.UI.Views
{
    public partial class PrintOperationsView : UserControl
    {
        public PrintOperationsView()
        {
            InitializeComponent();
        }

        private void IncreaseCopies_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is PrintOperationsViewModel vm && vm.NewJobCopies < 999)
                vm.NewJobCopies++;
        }

        private void DecreaseCopies_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is PrintOperationsViewModel vm && vm.NewJobCopies > 1)
                vm.NewJobCopies--;
        }

        private void CopiesTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !int.TryParse(e.Text, out _);
        }

        private void PageRangeTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !int.TryParse(e.Text, out _);
        }

        // ─── Drop Zone ──────────────────────────────────────────────────────────

        private void DropZone_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                if (sender is Border b)
                    b.BorderBrush = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x3B, 0x82, 0xF6));
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void DropZone_Drop(object sender, DragEventArgs e)
        {
            if (sender is Border b)
                b.BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x33, 0x41, 0x55));

            if (e.Data.GetDataPresent(DataFormats.FileDrop)
                && DataContext is PrintOperationsViewModel vm)
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files?.Length > 0 && File.Exists(files[0]))
                    vm.SelectedFilePath = files[0];
            }
            e.Handled = true;
        }

        // ─── Duplex / Color / Quality toggles ───────────────────────────────────

        private void SingleSided_Click(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is PrintOperationsViewModel vm) { vm.IsSingleSided = true; vm.IsDoubleSided = false; }
        }

        private void DoubleSided_Click(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is PrintOperationsViewModel vm) { vm.IsDoubleSided = true; vm.IsSingleSided = false; }
        }

        private void ColorPrint_Click(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is PrintOperationsViewModel vm) vm.IsColorPrint = true;
        }

        private void BwPrint_Click(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is PrintOperationsViewModel vm) vm.IsColorPrint = false;
        }

        private void QualityDraft_Click(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is PrintOperationsViewModel vm) vm.PrintQuality = "Draft";
        }

        private void QualityNormal_Click(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is PrintOperationsViewModel vm) vm.PrintQuality = "Normal";
        }

        private void QualityHigh_Click(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is PrintOperationsViewModel vm) vm.PrintQuality = "High";
        }

        // ─── Page range toggle ───────────────────────────────────────────────────

        private void PageRangeToggle_Click(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is PrintOperationsViewModel vm)
                vm.UsePageRange = !vm.UsePageRange;
        }

        // ─── Paper Size ──────────────────────────────────────────────────────────

        private void PaperA4_Click(object sender, MouseButtonEventArgs e)
        { if (DataContext is PrintOperationsViewModel vm) vm.PaperSize = "A4"; }

        private void PaperA3_Click(object sender, MouseButtonEventArgs e)
        { if (DataContext is PrintOperationsViewModel vm) vm.PaperSize = "A3"; }

        private void PaperLetter_Click(object sender, MouseButtonEventArgs e)
        { if (DataContext is PrintOperationsViewModel vm) vm.PaperSize = "Letter"; }

        private void PaperLegal_Click(object sender, MouseButtonEventArgs e)
        { if (DataContext is PrintOperationsViewModel vm) vm.PaperSize = "Legal"; }

        // ─── Orientation ─────────────────────────────────────────────────────────

        private void Portrait_Click(object sender, MouseButtonEventArgs e)
        { if (DataContext is PrintOperationsViewModel vm) { vm.IsPortrait = true; vm.IsLandscape = false; } }

        private void Landscape_Click(object sender, MouseButtonEventArgs e)
        { if (DataContext is PrintOperationsViewModel vm) { vm.IsLandscape = true; vm.IsPortrait = false; } }

        // ─── Collate ─────────────────────────────────────────────────────────────

        private void CollateOn_Click(object sender, MouseButtonEventArgs e)
        { if (DataContext is PrintOperationsViewModel vm) vm.Collate = true; }

        private void CollateOff_Click(object sender, MouseButtonEventArgs e)
        { if (DataContext is PrintOperationsViewModel vm) vm.Collate = false; }

        // ─── Fit to Page ─────────────────────────────────────────────────────────

        private void FitActual_Click(object sender, MouseButtonEventArgs e)
        { if (DataContext is PrintOperationsViewModel vm) vm.FitMode = "Actual"; }

        private void FitFit_Click(object sender, MouseButtonEventArgs e)
        { if (DataContext is PrintOperationsViewModel vm) vm.FitMode = "Fit"; }

        private void FitCustom_Click(object sender, MouseButtonEventArgs e)
        { if (DataContext is PrintOperationsViewModel vm) vm.FitMode = "Custom"; }

        // ─── Presets ─────────────────────────────────────────────────────────────

        private void PresetItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe
                && fe.DataContext is PrintPreset preset
                && DataContext is PrintOperationsViewModel vm)
                vm.ApplyPresetCommand.Execute(preset);
        }

        // ─── Print Order ─────────────────────────────────────────────────────────

        private void PrintOrderFirst_Click(object sender, MouseButtonEventArgs e)
        { if (DataContext is PrintOperationsViewModel vm) vm.PrintOrder = "FirstToLast"; }

        private void PrintOrderLast_Click(object sender, MouseButtonEventArgs e)
        { if (DataContext is PrintOperationsViewModel vm) vm.PrintOrder = "LastToFirst"; }

        // ─── N-up ────────────────────────────────────────────────────────────────

        private void NUp1_Click(object sender, MouseButtonEventArgs e)
        { if (DataContext is PrintOperationsViewModel vm) vm.NUpMode = 1; }

        private void NUp2_Click(object sender, MouseButtonEventArgs e)
        { if (DataContext is PrintOperationsViewModel vm) vm.NUpMode = 2; }

        private void NUp4_Click(object sender, MouseButtonEventArgs e)
        { if (DataContext is PrintOperationsViewModel vm) vm.NUpMode = 4; }

        // ─── Job Priority ────────────────────────────────────────────────────────

        private void PriorityLow_Click(object sender, MouseButtonEventArgs e)
        { if (DataContext is PrintOperationsViewModel vm) vm.JobPriority = "Low"; }

        private void PriorityNormal_Click(object sender, MouseButtonEventArgs e)
        { if (DataContext is PrintOperationsViewModel vm) vm.JobPriority = "Normal"; }

        private void PriorityHigh_Click(object sender, MouseButtonEventArgs e)
        { if (DataContext is PrintOperationsViewModel vm) vm.JobPriority = "High"; }

        // ─── Notifications ───────────────────────────────────────────────────────

        private void NotifyToggle_Click(object sender, MouseButtonEventArgs e)
        { if (DataContext is PrintOperationsViewModel vm) vm.NotifyOnComplete = !vm.NotifyOnComplete; }

        private void SoundToggle_Click(object sender, MouseButtonEventArgs e)
        { if (DataContext is PrintOperationsViewModel vm) vm.NotifyWithSound = !vm.NotifyWithSound; }
    }
}
