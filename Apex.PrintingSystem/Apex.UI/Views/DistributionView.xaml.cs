using System.Windows.Controls;
using System.Windows;
using Apex.UI.ViewModels;

namespace Apex.UI.Views
{
    public partial class DistributionView : UserControl
    {
        public DistributionView()
        {
            InitializeComponent();
        }

        private DistributionViewModel? ViewModel => DataContext as DistributionViewModel;

        private void DropZone_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                if (sender is Border border)
                {
                    border.BorderBrush = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3B82F6"));
                    border.Background = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#EFF6FF"));
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void DropZone_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is Border border)
            {
                border.BorderBrush = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#CBD5E1"));
                border.Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F8FAFC"));
            }
        }

        private void DropZone_Drop(object sender, DragEventArgs e)
        {
            // Reset border appearance
            DropZone_DragLeave(sender, e);

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0 && ViewModel != null)
                {
                    // Set the first dropped file
                    ViewModel.SetDroppedFile(files[0]);
                }
            }
        }
    }
}
