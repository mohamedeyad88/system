using Apex.Core.Interfaces;
using Apex.Core.Models;
using System.Threading.Tasks;
using System.Linq;

namespace Apex.Services
{
    public class SettingsService : ISettingsService
    {
        private readonly IRepository<SystemSettings> _settingsRepository;

        public SettingsService(IRepository<SystemSettings> settingsRepository)
        {
            _settingsRepository = settingsRepository;
        }

        public async Task<string> GetValueAsync(string key, string defaultValue = "")
        {
            var setting = (await _settingsRepository.FindAsync(s => s.Key == key)).FirstOrDefault();
            return setting?.Value ?? defaultValue;
        }

        public async Task SetValueAsync(string key, string value)
        {
            var setting = (await _settingsRepository.FindAsync(s => s.Key == key)).FirstOrDefault();
            if (setting == null)
            {
                setting = new SystemSettings { Key = key, Value = value };
                await _settingsRepository.AddAsync(setting);
            }
            else
            {
                setting.Value = value;
                await _settingsRepository.UpdateAsync(setting);
            }
        }

        // Restored method
        public async Task SaveSettingAsync(int key, string value)
        {
            // Assuming key is ID or we convert int key to string key
            // For now, let's treat int key as a string key
            await SetValueAsync(key.ToString(), value);
        }
    }
}
