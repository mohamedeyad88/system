using System;

namespace Apex.UI.Services
{
    /// <summary>
    /// The bridge that turns separate sections into one workflow: a producer section
    /// (Page Tools, Imposition) writes its finished PDF to a file and calls
    /// <see cref="SendToPrinting"/>; the shell (MainViewModel) then navigates to the
    /// Printing section and drops the file into its queue. Registered as a singleton
    /// so any section and the shell share the one instance.
    /// </summary>
    public sealed class WorkflowHandoff
    {
        /// <summary>Raised with a file path that should be added to the print queue.</summary>
        public event Action<string>? SendToPrintingRequested;

        public void SendToPrinting(string filePath) => SendToPrintingRequested?.Invoke(filePath);
    }
}
