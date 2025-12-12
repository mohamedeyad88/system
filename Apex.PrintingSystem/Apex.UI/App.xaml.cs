using System.Windows;
using System;

namespace Apex.UI
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                // Create simple window without DI for testing
                var mainWindow = new MainWindow();
                mainWindow.Show();
                
                MessageBox.Show("Application started successfully!", "Success", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                var errorMessage = $"Fatal Error:\n\n" +
                                 $"Message: {ex.Message}\n\n" +
                                 $"Type: {ex.GetType().Name}\n\n" +
                                 $"Source: {ex.Source}\n\n";
                
                if (ex.InnerException != null)
                {
                    errorMessage += $"Inner Exception: {ex.InnerException.Message}\n\n";
                }
                
                errorMessage += $"Stack Trace:\n{ex.StackTrace}";
                
                System.IO.File.WriteAllText(
                    @"D:\Apex\system\crash_log.txt", 
                    $"[{DateTime.Now}]\n{errorMessage}");
                
                MessageBox.Show(errorMessage, "Fatal Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                
                Shutdown();
            }
        }
    }
}
