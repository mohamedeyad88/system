using System.Threading.Tasks;

namespace Apex.Core.Interfaces
{
    public interface IDialogService
    {
        Task<bool> ShowConfirmationAsync(string title, string message, string confirmText = "Confirm", string cancelText = "Cancel", bool isDestructive = false);
        Task ShowAlertAsync(string title, string message);
    }
}
