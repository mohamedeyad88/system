using System;
using System.Threading.Tasks;
using System.Windows;
using Apex.UI.ViewModels;

namespace Apex.UI
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Windows.Input.Mouse.OverrideCursor = null;
                this.Cursor = System.Windows.Input.Cursors.Arrow;

                if (DataContext is MainViewModel mainViewModel)
                {
                    if (mainViewModel.CurrentViewModel == null)
                        mainViewModel.NavigateToDashboard();
                    // NOTE: InitializeAsync is handled by MainViewModel.OnCurrentViewModelChanged.
                    // Do NOT call it here — that causes two concurrent DB queries on the same
                    // DbContext instance and triggers InvalidOperationException.
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"خطأ في تحميل النافذة:\n{ex.Message}",
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }
}
