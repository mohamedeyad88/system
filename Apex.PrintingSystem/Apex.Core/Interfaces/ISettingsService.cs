using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apex.Core.Interfaces
{
    public interface ISettingsService
    {
        Task<string> GetValueAsync(string key, string defaultValue = "");
        Task SetValueAsync(string key, string value);

        /// <summary>Load all settings in a single query — avoids concurrent DbContext errors.</summary>
        Task<Dictionary<string, string>> GetAllSettingsAsync();
    }
}
