using System.Windows;

namespace Apex.AdminTool
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new LicenseGeneratorViewModel();

            if (DataContext is LicenseGeneratorViewModel vm)
                vm.LoadAuditLog();
        }

        private void PasteDeviceId_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is LicenseGeneratorViewModel vm)
                vm.DeviceId = Clipboard.GetText()?.Trim() ?? "";
        }
    }
}
