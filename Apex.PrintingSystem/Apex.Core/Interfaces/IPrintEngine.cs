using System.Threading.Tasks;

namespace Apex.Core.Interfaces
{
    public interface IPrintEngine
    {
        Task<bool> PrintAsync(string printerName, string filePath);
        Task<bool> PrintTestPageAsync(string printerName);
    }
}
