using System.Threading.Tasks;

namespace Apex.Core.Interfaces
{
    public interface IPrinterValidationService
    {
        Task<(bool IsValid, string Message)> ValidatePrinterAsync(string printerName);
    }
}
