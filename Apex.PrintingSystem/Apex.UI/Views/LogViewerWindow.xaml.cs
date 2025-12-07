using Apex.Core.Interfaces;
using Apex.UI.ViewModels;
using System.Windows;

namespace Apex.UI.Views
{
    public partial class LogViewerWindow : Window
    {
        public LogViewerWindow(ILoggerService loggerService, ILogReaderService logReader)
        {
            InitializeComponent();
            DataContext = new LogViewerViewModel(loggerService, logReader);
        }
    }
}
