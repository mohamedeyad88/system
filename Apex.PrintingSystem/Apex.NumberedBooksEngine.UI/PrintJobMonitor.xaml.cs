using Apex.NumberedBooksEngine.Core;
using System;
using System.Windows;

namespace Apex.NumberedBooksEngine.UI
{
    public partial class PrintJobMonitor : Window
    {
        private readonly IPrintOutputService _printService;

        public PrintJobMonitor(IPrintOutputService printService, long totalPages)
        {
            InitializeComponent();
            _printService = printService;

            // Subscribe to status changes
            _printService.StatusChanged += PrintService_StatusChanged;

            // Initialize with total pages
            UpdateStatus(new PrintJobStatus { TotalPages = totalPages });
        }

        private void PrintService_StatusChanged(object? sender, PrintJobStatus status)
        {
            // Marshal to UI thread
            Dispatcher.Invoke(() => UpdateStatus(status));
        }

        private void UpdateStatus(PrintJobStatus status)
        {
            double percent = status.TotalPages > 0
                ? (double)status.CurrentPage / status.TotalPages * 100
                : 0;

            ProgressBar.Value = percent;
            ProgressText.Text = $"{percent:F1}%";

            CurrentPageText.Text = $"{status.CurrentPage} / {status.TotalPages}";
            SpeedText.Text = $"{status.PagesPerSecond:F1} pages/sec";
            ElapsedTimeText.Text = status.ElapsedTime.ToString(@"hh\:mm\:ss");
            StatusText.Text = status.Status;
            ErrorsText.Text = string.IsNullOrEmpty(status.Error) ? "None" : status.Error;

            PauseButton.IsEnabled = !status.IsPaused && !status.IsCancelled && status.Status == "Printing";
            ResumeButton.IsEnabled = status.IsPaused;
            CancelButton.IsEnabled = !status.IsCancelled && status.Status != "Completed";
        }

        private void Pause_Click(object sender, RoutedEventArgs e)
        {
            _printService.Pause();
        }

        private void Resume_Click(object sender, RoutedEventArgs e)
        {
            _printService.Resume();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to cancel the print job?",
                "Confirm Cancel",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                _printService.Cancel();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _printService.StatusChanged -= PrintService_StatusChanged;
            base.OnClosed(e);
        }
    }
}
