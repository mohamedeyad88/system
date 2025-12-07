using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apex.Core.Interfaces
{
    public interface ILogReaderService
    {
        Task<string> ReadLogFileAsync(string logPath);
        Task<IEnumerable<string>> GetLogFilesAsync();
        Task<string> GetTodayLogPathAsync();
    }
}
