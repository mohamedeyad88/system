using Apex.Core.Enums;
using Apex.Core.Models;
using System.Threading.Tasks;

namespace Apex.Core.Interfaces
{
    public interface IJobDistributionService
    {
        Task DistributeJobAsync(PrintJob job);
        Task ProcessJobAsync(int jobId);

        // Priority-based job selection
        Task<PrintJob?> GetNextPendingJobAsync();
        Task<System.Collections.Generic.IEnumerable<PrintJob>> GetPendingJobsOrderedByPriorityAsync();

        // Retry failed jobs
        Task RetryFailedJobAsync(int jobId);
        Task RetryAllFailedJobsAsync();

        // Job cancellation
        Task<bool> CancelJobAsync(int jobId);
        Task<int> CancelAllPendingJobsAsync();
        Task<System.Collections.Generic.Dictionary<PrintJobStatus, int>> GetJobStatusCountsAsync();

        // Job creation with priority support
        PrintJob CreatePrintJob(string filePath, int copies, JobPriority priority = JobPriority.Normal);
        
        // Progress monitoring
        Task UpdateJobProgressAsync(int jobId, int currentPage, int totalPages);
        Task<PrintJobProgress?> GetJobProgressAsync(int jobId);
        Task<System.Collections.Generic.IEnumerable<PrintJobProgress>> GetAllPrintingJobsProgressAsync();
        
        Task UpdateJobStatus(int jobId, JobAssignmentStatus status);
        void AssignJobsToPrinters();
        void DistributeJobs();
    }
}
