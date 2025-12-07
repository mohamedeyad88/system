using Apex.Core.Interfaces;
using Apex.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apex.Data.Repositories
{
    public class SettingsRepository : Repository<SystemSettings>, IRepository<SystemSettings>
    {
        public SettingsRepository(ApexDbContext context) : base(context)
        {
        }
    }
}
