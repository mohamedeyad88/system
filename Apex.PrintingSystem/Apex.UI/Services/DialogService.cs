using System.Threading.Tasks;
using System.Windows;
using Apex.Core.Interfaces;
using Apex.UI.Views.Dialogs;

namespace Apex.UI.Services
{
    public class DialogService : IDialogService
    {
        public Task<bool> ShowConfirmationAsync(string title, string message, string confirmText = "Confirm", string cancelText = "Cancel", bool isDestructive = false)
        {
            var tcs = new TaskCompletionSource<bool>();

            Application.Current.Dispatcher.Invoke(() =>
            {
                var dialog = new ConfirmationDialog
                {
                    TitleText = title,
                    Message = message,
                    ConfirmText = confirmText,
                    CancelText = cancelText,
                    Owner = Application.Current.MainWindow
                };

                // If destructive, we could change style here
                if (isDestructive)
                {
                    // dialog.BtnConfirm.Style = ... (or bind it)
                }

                dialog.ShowDialog();
                tcs.SetResult(dialog.Result);
            });

            return tcs.Task;
        }

        public Task ShowAlertAsync(string title, string message)
        {
            return ShowConfirmationAsync(title, message, "OK", "", false);
        }
    }
}
