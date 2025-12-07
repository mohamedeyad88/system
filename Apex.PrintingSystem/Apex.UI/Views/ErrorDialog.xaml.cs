using System;
using System.Windows;

namespace Apex.UI.Views
{
    public partial class ErrorDialog : Window
    {
        public ErrorDialog(string message, string stackTrace)
        {
            InitializeComponent();
            ErrorDetails.Text = $"{message}\n\nStack Trace:\n{stackTrace}";
        }

        private void ViewLogs_Click(object sender, RoutedEventArgs e)
        {
            // Open logs folder
            var logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            if (System.IO.Directory.Exists(logPath))
            {
                System.Diagnostics.Process.Start("explorer.exe", logPath);
            }
        }

        private void Ignore_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.Close();
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}
