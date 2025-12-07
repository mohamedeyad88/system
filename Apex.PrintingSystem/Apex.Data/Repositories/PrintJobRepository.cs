using Apex.Core.Interfaces;
using Apex.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apex.Data.Repositories
{
    public class PrintJobRepository : Repository<PrintJob>, IRepository<PrintJob>
    {
        public PrintJobRepository(ApexDbContext context) : base(context)
        {
        }
    }
}
