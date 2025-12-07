using CommunityToolkit.Mvvm.ComponentModel;
using System.Threading.Tasks;

namespace Apex.UI.ViewModels
{
    public class ViewModelBase : ObservableObject
    {
        /// <summary>
        /// Override this method in derived ViewModels to perform async initialization.
        /// This should be called after the ViewModel is constructed and the UI is ready.
        /// </summary>
        public virtual Task InitializeAsync()
        {
            return Task.CompletedTask;
        }
    }
}
