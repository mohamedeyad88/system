using Apex.Core.Interfaces;
using Microsoft.Win32;

namespace Apex.UI.Services
{
    /// <summary>
    /// WPF implementation of <see cref="IFileDialogService"/> backed by the Win32
    /// common file dialogs. The only place native dialogs are created, so ViewModels
    /// depend on the abstraction and remain unit-testable.
    /// </summary>
    public sealed class FileDialogService : IFileDialogService
    {
        public string? OpenFile(string title, string filter, string? initialDirectory = null)
        {
            var dlg = new OpenFileDialog
            {
                Title = title,
                Filter = filter,
                Multiselect = false
            };
            if (!string.IsNullOrWhiteSpace(initialDirectory))
                dlg.InitialDirectory = initialDirectory;

            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        public string? SaveFile(string title, string filter, string? suggestedFileName = null)
        {
            var dlg = new SaveFileDialog
            {
                Title = title,
                Filter = filter
            };
            if (!string.IsNullOrWhiteSpace(suggestedFileName))
                dlg.FileName = suggestedFileName;

            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }
    }
}
