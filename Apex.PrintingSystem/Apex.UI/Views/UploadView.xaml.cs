using System.Windows;
using System.Windows.Controls;
using Apex.UI.ViewModels;

namespace Apex.UI.Views
{
    public partial class UploadView : UserControl
    {
        public UploadView()
        {
            InitializeComponent();
        }

        // Handle Drop event manually to extract file paths if Behavior doesn't work out of the box with string[]
        private void Border_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (DataContext is PrintManagerViewModel vm)
                {
                    vm.DropFilesCommand.Execute(files);
                }
            }
        }
    }
}
