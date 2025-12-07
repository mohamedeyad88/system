using System.Threading.Tasks;

namespace Apex.Core.Interfaces
{
    public interface IDatabaseHealthService
    {
        Task<bool> CheckHealthAsync();
        Task BackupDatabaseAsync();
        Task RepairDatabaseAsync();
    }
}
