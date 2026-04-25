using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Apex.Services.Users;
using System.Threading.Tasks;
using System.Windows;

namespace Apex.UI.ViewModels
{
    public partial class LoginViewModel : ObservableObject
    {
        [ObservableProperty] private string _username = "";
        [ObservableProperty] private string _password = "";
        [ObservableProperty] private string _errorMessage = "";
        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private bool _loginSuccess;

        [RelayCommand]
        private async Task LoginAsync()
        {
            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
            {
                ErrorMessage = "يرجى إدخال اسم المستخدم وكلمة المرور";
                return;
            }

            IsLoading    = true;
            ErrorMessage = "";

            // Run auth on background thread
            bool ok = await Task.Run(() => UserSessionManager.Instance.Login(Username, Password));

            // Force ALL post-login property changes onto the UI thread.
            // CommunityToolkit AsyncRelayCommand can resume on a ThreadPool thread even
            // after await, so we explicitly marshal back rather than relying on
            // SynchronizationContext capture.
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IsLoading = false;
                if (ok)
                    LoginSuccess = true;
                else
                    ErrorMessage = "اسم المستخدم أو كلمة المرور غير صحيحة";
            });
        }

        [RelayCommand]
        private void LoginAsGuest()
        {
            UserSessionManager.Instance.LoginAsGuest();
            LoginSuccess = true;
        }
    }
}
