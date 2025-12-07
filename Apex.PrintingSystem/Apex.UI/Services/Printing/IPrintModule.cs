using System.Threading.Tasks;

namespace Apex.UI.Services.Printing
{
    public interface IPrintModule
    {
        string ModuleName { get; }
        bool IsRunning { get; }
        Task StartAsync();
        Task StopAsync();
    }
}
