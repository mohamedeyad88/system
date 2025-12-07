using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Core.Interfaces
{
    public interface INavigationService
    {
        ObservableObject CurrentViewModel { get; }
        void NavigateTo<T>() where T : ObservableObject;
    }
}
