using Apex.NumberedBooksEngine.Core;
using Apex.NumberedBooksEngine.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.NumberedBooksEngine
{
    public interface INumberedBooksService
    {
        Task<JobResult> GenerateNumberedBooksAsync(BookJobOptions options, IProgress<ProgressInfo> progress, CancellationToken cancellationToken);
    }

    public class NumberedBooksService : INumberedBooksService
    {
        public async Task<JobResult> GenerateNumberedBooksAsync(BookJobOptions options, IProgress<ProgressInfo> progress, CancellationToken cancellationToken)
        {
            var orchestrator = new JobOrchestrator();
            return await orchestrator.RunJobAsync(options, progress, cancellationToken);
        }
    }
}
