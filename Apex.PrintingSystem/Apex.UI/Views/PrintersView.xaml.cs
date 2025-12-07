using System.Windows.Controls;

namespace Apex.UI.Views
{
    public partial class PrintersView : UserControl
    {
        public PrintersView()
        {
            InitializeComponent();
            Loaded += PrintersView_Loaded;
        }

        private async void PrintersView_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ViewModelBase viewModel)
            {
                await viewModel.InitializeAsync();
            }
        }
    }
}
