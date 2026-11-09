using System;

namespace Apex.NumberedBooksEngine.Core
{
    /// <summary>
    /// The live controls a running streaming print job hands back to its caller.
    ///
    /// <para>The streaming print path holds on the print thread when a tray reaches its
    /// operator-declared load (the firmware-independent refill safeguard). Before this
    /// existed, the UI had no handle on that running job: it could not see the hold and had
    /// no way to release it, so a job that paused for a refill looked, from the floor, like
    /// it had stopped by itself after exactly N sheets. This hands the view a way to show the
    /// hold and to resume it once the tray is refilled.</para>
    /// </summary>
    public interface IStreamingPrintControl
    {
        /// <summary>Clears a refill hold (also the "I refilled the tray" confirmation) and carries on.</summary>
        void Resume();

        /// <summary>Holds the run at the next page boundary.</summary>
        void Pause();

        /// <summary>True while the run is held (refill or operator pause).</summary>
        bool IsPaused { get; }

        /// <summary>A human-readable reason for the current hold, or null when running.</summary>
        string? HoldReason { get; }

        /// <summary>Raised on every status change so the UI can reflect a hold the moment it starts.</summary>
        event EventHandler<PrintJobStatus>? StatusChanged;
    }
}
