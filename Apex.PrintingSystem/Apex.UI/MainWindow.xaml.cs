using System.Windows;
using Apex.UI.ViewModels;
using System;
using System.IO;

namespace Apex.UI
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Fix for cursor stuck in "Wait" state after startup
                System.Windows.Input.Mouse.OverrideCursor = null;
                this.Cursor = System.Windows.Input.Cursors.Arrow;

                // Initialize the current ViewModel asynchronously
                if (DataContext is MainViewModel mainViewModel && mainViewModel.CurrentViewModel != null)
                {
                    File.AppendAllText("startup.log", $"[{DateTime.Now}] Initializing ViewModel: {mainViewModel.CurrentViewModel.GetType().Name}...\n");
                    await mainViewModel.CurrentViewModel.InitializeAsync();
                    File.AppendAllText("startup.log", $"[{DateTime.Now}] ViewModel initialized successfully.\n");
                }
            }
            catch (Exception ex)
            {
                File.WriteAllText("viewmodel_init_error.log", $"[{DateTime.Now}] Error initializing ViewModel:\n{ex}");
                MessageBox.Show($"Error loading data: {ex.Message}", "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}