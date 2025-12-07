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
        private string _fullLogContent = "";

        [ObservableProperty] private string _logContent = "";
        [ObservableProperty] private string _logPath = "";
        [ObservableProperty] private bool _autoRefresh = true;
        [ObservableProperty] private string _searchText = "";
        [ObservableProperty] private LogLevelFilter _selectedLogLevel = LogLevelFilter.All;

        public System.Collections.Generic.IEnumerable<LogLevelFilter> LogLevels => Enum.GetValues(typeof(LogLevelFilter)).Cast<LogLevelFilter>();

        public LogViewerViewModel(ILoggerService loggerService, ILogReaderService logReader)
        {
            _loggerService = loggerService;
            _logReader = logReader;
            LogPath = _loggerService.GetTodayLogPath();
            
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
                _fullLogContent = await _logReader.ReadLogFileAsync(LogPath);
                FilterLogs();
            }
            catch (Exception ex)
            {
                LogContent = $"Error reading log: {ex.Message}";
            }
        }

        private void FilterLogs()
        {
            if (string.IsNullOrEmpty(_fullLogContent))
            {
                LogContent = "";
                return;
            }

            var lines = _fullLogContent.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
            var filtered = lines.Where(line =>
            {
                if (SelectedLogLevel != LogLevelFilter.All && !line.Contains($"[{SelectedLogLevel}]", StringComparison.OrdinalIgnoreCase)) return false;
                if (!string.IsNullOrWhiteSpace(SearchText) && !line.Contains(SearchText, StringComparison.OrdinalIgnoreCase)) return false;
                return true;
            });

            LogContent = string.Join(Environment.NewLine, filtered);
        }

        [RelayCommand]
        public void OpenLogFolder()
        {
            try
            {
                var folder = Path.GetDirectoryName(LogPath);
                if (folder != null && Directory.Exists(folder))
                {
                    Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
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
