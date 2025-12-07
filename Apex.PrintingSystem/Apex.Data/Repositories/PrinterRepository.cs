using Apex.Core.Interfaces;
using Apex.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apex.Data.Repositories
{
    public class PrinterRepository : Repository<Printer>, IRepository<Printer>
    {
        public PrinterRepository(ApexDbContext context) : base(context)
        {
        }
    }
}
