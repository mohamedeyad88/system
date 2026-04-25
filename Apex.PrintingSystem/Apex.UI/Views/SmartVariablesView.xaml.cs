using System.Windows.Controls;
using Apex.UI.ViewModels;

namespace Apex.UI.Views
{
    public partial class SmartVariablesView : UserControl
    {
        public SmartVariablesView()
        {
            InitializeComponent();
        }

        // DataContext is SmartVariablesViewModel (set by parent TemplateDesignerView)
        private SmartVariablesViewModel? VM => DataContext as SmartVariablesViewModel;
    }
}
