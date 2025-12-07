using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.Core.Interfaces
{
    public class JobCreateOptions
    {
        public string TargetPool { get; set; } = string.Empty;
        public IEnumerable<string> TargetPrinters { get; set; } = new List<string>();
        public bool MergeAsOne { get; set; }
        public string SavedQueueName { get; set; } = string.Empty;
        public string Creator { get; set; } = string.Empty;
        public Dictionary<string, object> GlobalOptions { get; set; } = new Dictionary<string, object>();
    }

    public interface IPrintJobManager
    {
        Task<Guid> CreateJobsAsync(IEnumerable<string> filePaths, JobCreateOptions options);
        Task StartProcessingAsync(CancellationToken ct);
        Task RetryJobAsync(Guid jobGuid);
    }
}
