using Apex.Core.Interfaces;
using System;
using System.Diagnostics;
using System.Windows;

namespace Apex.Services
{
    public class ExceptionRecoveryService
    {
        private readonly ILoggerService _logger;

        public ExceptionRecoveryService(ILoggerService logger)
        {
            _logger = logger;
        }

        public void RecoverFromUIException(Exception ex)
        {
            _logger.Log(LogLevel.Critical, "Attempting UI Recovery", "ExceptionRecoveryService", "RecoverFromUIException", ex);
            
            // Logic to clear transient state or reset ViewModels could go here
            // For now, we just log and allow the user to continue via the dialog
        }

        public void RestartApplication()
        {
            _logger.Log(LogLevel.Critical, "Restarting Application...", "ExceptionRecoveryService", "RestartApplication");
            Process.Start(Process.GetCurrentProcess().MainModule?.FileName ?? "Apex.UI.exe");
            Application.Current.Shutdown();
        }
    }
}
