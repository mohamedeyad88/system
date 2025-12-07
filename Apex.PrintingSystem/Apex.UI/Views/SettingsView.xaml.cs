using System.Windows.Controls;

namespace Apex.UI.Views
{
    public partial class SettingsView : UserControl
    {
        public SettingsView()
        {
            InitializeComponent();
            Loaded += SettingsView_Loaded;
        }

        private async void SettingsView_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ViewModelBase viewModel)
            {
                await viewModel.InitializeAsync();
            }
        }
    }
}
