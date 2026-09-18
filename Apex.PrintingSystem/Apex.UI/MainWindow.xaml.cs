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
            Closing += MainWindow_Closing;
        }

        /// <summary>
        /// A print run lives and dies with this process, so closing the window while
        /// one is in flight throws the rest of the order away. Ask first, and default
        /// to staying open.
        /// </summary>
        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;

            MainViewModel.ShutdownPrompt prompt;
            try { prompt = vm.CurrentShutdownPrompt(); }
            catch { return; }   // never trap the operator in a window that will not close

            if (!prompt.Ask) return;

            var answer = MessageBox.Show(
                ViewModelBase.Lf("Shell_ExitDuringPrintBody", prompt.OutstandingCopies),
                ViewModelBase.L("Shell_ExitDuringPrintTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (answer != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }

            vm.AbandonPrintRunForShutdown();
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
