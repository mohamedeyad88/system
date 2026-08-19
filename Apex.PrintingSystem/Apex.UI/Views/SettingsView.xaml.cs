using System.Windows.Controls;

namespace Apex.UI.Views
{
    public partial class SettingsView : UserControl
    {
        // Initialisation belongs to MainViewModel.OnCurrentViewModelChanged, which is
        // the convention the rest of the views follow (see MainWindow.xaml.cs).
        //
        // This view also called InitializeAsync from its Loaded handler. MainViewModel
        // runs it on a background thread via Task.Run while Loaded runs on the UI
        // thread, so opening Settings started two concurrent reads on the same
        // DbContext and threw "A second operation was started on this context
        // instance… different threads concurrently using the same instance".
        public SettingsView()
        {
            InitializeComponent();
        }
    }
}
