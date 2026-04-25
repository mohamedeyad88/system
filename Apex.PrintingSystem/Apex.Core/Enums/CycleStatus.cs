namespace Apex.Core.Enums
{
    /// <summary>
    /// Status of a printing cycle (one number with all its copies).
    /// 
    /// CRITICAL DISTINCTION:
    /// - CompletedPhysical: Cycle تمت طباعته فعلياً ✅
    /// - Skipped: Cycle تم تخطيه (لم تتم الطباعة لكن منطقياً مكتمل) ⏭️
    /// - CompletedLogical = CompletedPhysical || Skipped (للـ Dependency Resolution)
    /// </summary>
    public enum CycleStatus
    {
        /// <summary>
        /// Cycle is waiting to be executed.
        /// </summary>
        Pending = 0,

        /// <summary>
        /// Cycle is currently being printed.
        /// </summary>
        Printing = 1,

        /// <summary>
        /// ✅ Cycle completed successfully - physically printed.
        /// This is the only status that means actual printing happened.
        /// </summary>
        CompletedPhysical = 2,

        /// <summary>
        /// ⏭️ Cycle was skipped (not printed, but logically complete for dependencies).
        /// Used when user chooses to skip a failed cycle.
        /// </summary>
        Skipped = 3,

        /// <summary>
        /// ❌ Cycle failed during printing.
        /// </summary>
        Failed = 4,

        /// <summary>
        /// Cycle is paused (temporary state).
        /// </summary>
        Paused = 5,

        /// <summary>
        /// Cycle is being retried after a failure.
        /// </summary>
        Retrying = 6
    }

    /// <summary>
    /// Extension methods for CycleStatus.
    /// </summary>
    public static class CycleStatusExtensions
    {
        /// <summary>
        /// Checks if a cycle is logically complete (can proceed to next cycle).
        /// CompletedLogical = CompletedPhysical || Skipped
        /// </summary>
        public static bool IsCompletedLogical(this CycleStatus status)
        {
            return status == CycleStatus.CompletedPhysical || status == CycleStatus.Skipped;
        }

        /// <summary>
        /// Checks if a cycle is physically printed.
        /// </summary>
        public static bool IsCompletedPhysical(this CycleStatus status)
        {
            return status == CycleStatus.CompletedPhysical;
        }

        /// <summary>
        /// Checks if a cycle is in a terminal state (won't change).
        /// </summary>
        public static bool IsTerminal(this CycleStatus status)
        {
            return status == CycleStatus.CompletedPhysical 
                || status == CycleStatus.Skipped 
                || status == CycleStatus.Failed;
        }

        /// <summary>
        /// Checks if a cycle is in an active state (currently processing).
        /// </summary>
        public static bool IsActive(this CycleStatus status)
        {
            return status == CycleStatus.Printing || status == CycleStatus.Retrying;
        }
    }
}

