using Apex.Core.Interfaces;
using Apex.Core.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Apex.Services
{
    public class SettingsService : ISettingsService
    {
        private readonly IRepository<SystemSettings> _settingsRepository;

        public SettingsService(IRepository<SystemSettings> settingsRepository)
        {
            _settingsRepository = settingsRepository;
        }

        /// <summary>Load ALL settings in a single DB round-trip.</summary>
        public async Task<Dictionary<string, string>> GetAllSettingsAsync()
        {
            var all = await _settingsRepository.GetAllAsync();
            return all.ToDictionary(s => s.Key, s => s.Value);
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
                await _settingsRepository.AddAsync(new SystemSettings { Key = key, Value = value });
            }
            else
            {
                setting.Value = value;
                await _settingsRepository.UpdateAsync(setting);
            }
        }

    }
}
