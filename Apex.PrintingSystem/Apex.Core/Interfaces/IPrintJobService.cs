using Apex.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apex.Core.Interfaces
{
    public interface IPrintJobService
    {
        Task<IEnumerable<PrintJob>> GetAllPrintJobsAsync();
        Task<IEnumerable<PrintJob>> GetPendingPrintJobsAsync();
        Task<PrintJob?> GetPrintJobByIdAsync(int id);
        Task CreatePrintJobAsync(PrintJob printJob);
        Task UpdatePrintJobAsync(PrintJob printJob);
        Task DeletePrintJobAsync(int id);
    }
}
