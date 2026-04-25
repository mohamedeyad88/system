using System.Windows;
using Apex.UI.ViewModels;

namespace Apex.UI.Views
{
    /// <summary>Code-behind for the Activation window.</summary>
    public partial class ActivationView : Window
    {
        public ActivationView()
        {
            InitializeComponent();
        }

        public ActivationView(ActivationViewModel viewModel) : this()
        {
            DataContext = viewModel;
            viewModel.Initialize();
        }
    }
}
