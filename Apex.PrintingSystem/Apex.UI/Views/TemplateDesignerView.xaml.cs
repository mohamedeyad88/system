using Apex.UI.ViewModels;
using Microsoft.Win32;
using System.Windows.Controls;

namespace Apex.UI.Views
{
    public partial class TemplateDesignerView : UserControl
    {
        public TemplateDesignerView()
        {
            InitializeComponent();

            // Wire file-dialog buttons (cannot bind file paths in XAML)
            BtnImport.Click    += BtnImport_Click;
            BtnUploadBg.Click  += BtnUploadBg_Click;
        }

        private void BtnImport_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title       = "استيراد قالب Apex",
                Filter      = "Apex Template (*.apext)|*.apext|كل الملفات|*.*",
                Multiselect = false
            };
            if (dlg.ShowDialog() != true) return;
            if (DataContext is TemplateDesignerViewModel vm)
                vm.ImportTemplateCommand.Execute(dlg.FileName);
        }

        private void BtnUploadBg_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title  = "اختر صورة الخلفية",
                Filter = "صور|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tiff|كل الملفات|*.*"
            };
            if (dlg.ShowDialog() != true) return;
            if (DataContext is TemplateDesignerViewModel vm)
                vm.SetBackgroundCommand.Execute(dlg.FileName);
        }
    }
}
