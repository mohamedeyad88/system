using System.Windows.Controls;
using Apex.UI.ViewModels;

namespace Apex.UI.Views
{
    /// <summary>
    /// Printer Monitor View - displays live status of all connected printers
    /// </summary>
    public partial class DistributionView : UserControl
    {
        public DistributionView()
        {
            InitializeComponent();
        }

        private DistributionViewModel? ViewModel => DataContext as DistributionViewModel;
    }
}
