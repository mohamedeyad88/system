using Apex.Core.Models;
using System.Threading.Tasks;

namespace Apex.Core.Interfaces
{
    public interface IRoutingRulesEngine
    {
        Task<RoutingRule?> EvaluateRulesAsync(PrintJob job);
    }
}
