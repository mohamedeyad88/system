using Apex.Core.Enums;

namespace Apex.Core.Models
{
    public class JobAssignment
    {
        public int Id { get; set; }
        public int PrintJobId { get; set; }
        public int PrinterId { get; set; }
        public int AssignedCopies { get; set; }
        public int CompletedCopies { get; set; }
        public JobAssignmentStatus Status { get; set; } = JobAssignmentStatus.Pending;

        public virtual PrintJob PrintJob { get; set; } = null!;
        public virtual Printer Printer { get; set; } = null!;
    }
}
