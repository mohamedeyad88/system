using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Apex.UI.ViewModels;

namespace Apex.UI.Views
{
    public partial class UploadView : UserControl
    {
        public UploadView()
        {
            InitializeComponent();
        }

        // Drag-drop zone handler
        private void Border_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (DataContext is PrintManagerViewModel vm)
                vm.DropFilesCommand.Execute(files);
        }

        // Double-click on DataGrid row → preview file
        private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is PrintManagerViewModel vm &&
                FilesGrid.SelectedItem is IngestedFileItem item)
            {
                vm.PreviewFileCommand.Execute(item);
            }
        }
    }
}
