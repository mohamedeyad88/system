using Apex.UI.ViewModels;
using System.Windows;
using System.Windows.Media;

namespace Apex.UI.Views
{
    public partial class LoginWindow : Window
    {
        private readonly LoginViewModel _vm;

        private static readonly SolidColorBrush FocusBrush  = new(Color.FromRgb(0x25, 0x63, 0xEB));
        private static readonly SolidColorBrush NormalBrush = new(Color.FromRgb(0x33, 0x41, 0x55));
        private static readonly SolidColorBrush ErrorBrush  = new(Color.FromRgb(0x7F, 0x1D, 0x1D));

        public LoginWindow()
        {
            InitializeComponent();
            _vm = new LoginViewModel();
            DataContext = _vm;

            // ── Thread-safe: close window when login succeeds ──────────────
            _vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(LoginViewModel.LoginSuccess) && _vm.LoginSuccess)
                {
                    Dispatcher.Invoke(() =>
                    {
                        DialogResult = true;
                        Close();
                    });
                }

                if (e.PropertyName == nameof(LoginViewModel.ErrorMessage))
                {
                    Dispatcher.Invoke(() =>
                    {
                        bool hasError = !string.IsNullOrEmpty(_vm.ErrorMessage);
                        ErrorBorder.Visibility = hasError ? Visibility.Visible : Visibility.Collapsed;
                        ErrorText.Text         = _vm.ErrorMessage;

                        // Highlight borders red on error
                        UsernameBorder.BorderBrush = hasError ? ErrorBrush : NormalBrush;
                        PasswordBorder.BorderBrush = hasError ? ErrorBrush : NormalBrush;
                    });
                }

                if (e.PropertyName == nameof(LoginViewModel.IsLoading))
                {
                    Dispatcher.Invoke(() =>
                    {
                        LoginBtn.IsEnabled = !_vm.IsLoading;
                    });
                }
            };

            // ── PasswordBox can't data-bind Password — use code-behind ─────
            PasswordBox.PasswordChanged += (s, e) =>
            {
                _vm.Password = PasswordBox.Password;
            };
        }

        // ── Focus border highlight ─────────────────────────────────────────
        private void UsernameBox_GotFocus(object sender, RoutedEventArgs e)
            => UsernameBorder.BorderBrush = FocusBrush;

        private void UsernameBox_LostFocus(object sender, RoutedEventArgs e)
            => UsernameBorder.BorderBrush = NormalBrush;

        private void PasswordBox_GotFocus(object sender, RoutedEventArgs e)
            => PasswordBorder.BorderBrush = FocusBrush;

        private void PasswordBox_LostFocus(object sender, RoutedEventArgs e)
            => PasswordBorder.BorderBrush = NormalBrush;

        // ── Close / cancel ─────────────────────────────────────────────────
        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
