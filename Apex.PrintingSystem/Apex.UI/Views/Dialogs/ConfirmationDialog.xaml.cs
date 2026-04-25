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

        private string _titleText = "Confirmation";
        public string TitleText
        {
            get => _titleText;
            set
            {
                _titleText = value;
                OnPropertyChanged();
            }
        }

        private string _message = "Are you sure?";
        public string Message
        {
            get => _message;
            set
            {
                _message = value;
                OnPropertyChanged();
            }
        }

        public string ConfirmText { get; set; } = "Confirm";
        public string CancelText { get; set; } = "Cancel";

        public bool Result { get; private set; } = false;

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Result = true;
                Close();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error closing dialog: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Result = false;
                Close();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error closing dialog: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
