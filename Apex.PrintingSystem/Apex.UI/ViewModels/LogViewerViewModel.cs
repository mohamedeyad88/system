using Apex.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Apex.UI.ViewModels
{
    public partial class LogViewerViewModel : ObservableObject
    {
        private readonly ILoggerService _loggerService;
        private readonly ILogReaderService _logReader;
        private readonly DispatcherTimer _timer;
        private System.Collections.Generic.IReadOnlyList<LogEntryMerge.Entry> _entries =
            System.Array.Empty<LogEntryMerge.Entry>();

        [ObservableProperty] private string _logContent = "";
        [ObservableProperty] private string _logPath = "";
        [ObservableProperty] private string _printLogPath = "";
        [ObservableProperty] private bool _autoRefresh = true;
        [ObservableProperty] private string _searchText = "";
        [ObservableProperty] private LogLevelFilter _selectedLogLevel = LogLevelFilter.All;

        public System.Collections.Generic.IEnumerable<LogLevelFilter> LogLevels => Enum.GetValues(typeof(LogLevelFilter)).Cast<LogLevelFilter>();

        public LogViewerViewModel(ILoggerService loggerService, ILogReaderService logReader)
        {
            _loggerService = loggerService ?? throw new System.ArgumentNullException(nameof(loggerService));
            _logReader = logReader ?? throw new System.ArgumentNullException(nameof(logReader));
            LogPath = _loggerService.GetTodayLogPath();
            PrintLogPath = TodayPrintLogPath;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _timer.Tick += async (s, e) =>
            {
                try { if (AutoRefresh) await RefreshAsync(); }
                catch (ObjectDisposedException) { _timer.Stop(); }
                catch { /* Ignore */ }
            };
            _timer.Start();

            // Fire and forget initial load
            Task.Run(RefreshAsync);
        }

        partial void OnSearchTextChanged(string value) => FilterLogs();
        partial void OnSelectedLogLevelChanged(LogLevelFilter value) => FilterLogs();

        [RelayCommand]
        public async Task RefreshAsync()
        {
            try
            {
                // Both logs, not just the application one. The print log is the file
                // that says why a job failed, and it lives elsewhere in its own format.
                var app = await _logReader.ReadLogFileAsync(LogPath);
                var print = await _logReader.ReadLogFileAsync(PrintLogPath);

                _entries = LogEntryMerge.Merge(app, print);
                FilterLogs();
            }
            catch (Exception ex)
            {
                LogContent = $"Error reading log: {ex.Message}";
            }
        }

        /// <summary>Today's print log, written by PrintLogger under %LocalAppData%.</summary>
        public static string TodayPrintLogPath => System.IO.Path.Combine(
            Apex.Services.Logging.PrintLogger.LogFolder,
            $"apex-printing-{DateTime.Now:yyyyMMdd}.log");

        private void FilterLogs()
        {
            if (_entries.Count == 0)
            {
                LogContent = "";
                return;
            }

            var filtered = _entries.Where(e =>
            {
                if (!LogEntryMerge.MatchesLevel(e, SelectedLogLevel)) return false;
                if (!string.IsNullOrWhiteSpace(SearchText) &&
                    !e.Text.Contains(SearchText, StringComparison.OrdinalIgnoreCase)) return false;
                return true;
            });

            LogContent = LogEntryMerge.Render(filtered);
        }

        [RelayCommand]
        public void OpenLogFolder()
        {
            // Prefer the print log's folder: someone opening this is almost always
            // chasing a print failure, and that is the file worth sending on.
            try
            {
                foreach (var candidate in new[] { PrintLogPath, LogPath })
                {
                    var folder = Path.GetDirectoryName(candidate);
                    if (folder != null && Directory.Exists(folder))
                    {
                        Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
                        return;
                    }
                }
            }
            catch { }
        }
    }

    public enum LogLevelFilter
    {
        All,
        Info,
        Warning,
        Error,
        Critical
    }
}
