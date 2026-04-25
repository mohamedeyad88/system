using Apex.UI.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace Apex.UI.Views
{
    public partial class UserManagementView : UserControl
    {
        public UserManagementView()
        {
            InitializeComponent();

            // PasswordBox cannot data-bind Password — wire it in code-behind
            PasswordBox.PasswordChanged += (s, e) =>
            {
                if (DataContext is UserManagementViewModel vm)
                    vm.EditPassword = PasswordBox.Password;
            };

            // Clear PasswordBox whenever the edit form opens
            DataContextChanged += (s, e) =>
            {
                if (e.NewValue is UserManagementViewModel vm)
                    vm.PropertyChanged += (_, pe) =>
                    {
                        if (pe.PropertyName == nameof(UserManagementViewModel.IsEditing))
                            Dispatcher.Invoke(() => PasswordBox.Clear());
                    };
            };
        }
    }
}
