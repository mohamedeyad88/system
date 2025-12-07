using System.Threading.Tasks;

namespace Apex.Core.Interfaces
{
    public interface ISettingsService
    {
        Task<string> GetValueAsync(string key, string defaultValue = "");
        Task SetValueAsync(string key, string value);
        
        // Restored method
        Task SaveSettingAsync(int key, string value);
    }
}
