using System.Windows;

namespace Apex.UI.Views.Dialogs
{
    /// <summary>Read-only viewer for the End-User License Agreement.</summary>
    public partial class EulaWindow : Window
    {
        public EulaWindow()
        {
            InitializeComponent();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
