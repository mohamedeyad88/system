using System.Windows;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Apex.UI.Views.Dialogs
{
    public partial class ConfirmationDialog : Window, INotifyPropertyChanged
    {
        public ConfirmationDialog()
        {
            InitializeComponent();
            DataContext = this;
        }

        public string TitleText { get; set; } = "Confirmation";
        public string Message { get; set; } = "Are you sure?";
        public string ConfirmText { get; set; } = "Confirm";
        public string CancelText { get; set; } = "Cancel";
        
        // IsDestructive logic can be handled by swapping styles in code-behind if needed, 
        // or binding the Confirm Button Style. For simplicity, we use Style.

        public bool Result { get; private set; } = false;

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            Result = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Result = false;
            Close();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
