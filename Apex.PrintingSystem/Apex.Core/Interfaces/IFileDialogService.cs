namespace Apex.Core.Interfaces
{
    /// <summary>
    /// Abstraction over native open/save file dialogs so that ViewModels stay testable
    /// (a fake implementation can be injected in unit tests instead of showing real UI).
    /// Implementations return the chosen absolute path, or <c>null</c> when the user cancels.
    /// </summary>
    public interface IFileDialogService
    {
        /// <summary>Shows an open-file dialog. Returns the chosen path or null if cancelled.</summary>
        /// <param name="title">Dialog title.</param>
        /// <param name="filter">Win32 filter string, e.g. "PDF|*.pdf|All|*.*".</param>
        /// <param name="initialDirectory">Optional starting directory.</param>
        string? OpenFile(string title, string filter, string? initialDirectory = null);

        /// <summary>Shows a save-file dialog. Returns the chosen path or null if cancelled.</summary>
        /// <param name="title">Dialog title.</param>
        /// <param name="filter">Win32 filter string.</param>
        /// <param name="suggestedFileName">Optional default file name.</param>
        string? SaveFile(string title, string filter, string? suggestedFileName = null);
    }
}
