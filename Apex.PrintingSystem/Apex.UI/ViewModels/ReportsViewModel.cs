using Apex.Services.Analytics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Apex.UI.ViewModels
{
    public partial class ReportsViewModel : ViewModelBase
    {
        public ReportsViewModel()
        {
            ScheduledReportManager.Instance.ReportGenerated += (_, result) =>
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    RecentResults.Insert(0, result);
                    while (RecentResults.Count > 20) RecentResults.RemoveAt(RecentResults.Count - 1);
                });
        }

        // ── Properties ────────────────────────────────────────────────

        [ObservableProperty] private ObservableCollection<ReportDefinition> _reportDefinitions = new();
        [ObservableProperty] private ReportDefinition? _selectedDefinition;
        [ObservableProperty] private bool _isGenerating = false;
        [ObservableProperty] private string _statusText = "\u062c\u0627\u0647\u0632";
        [ObservableProperty] private string _lastGeneratedFilePath = "";
        [ObservableProperty] private ObservableCollection<ReportResult> _recentResults = new();

        // New report form
        [ObservableProperty] private string _newReportName = "";
        [ObservableProperty] private ReportType _newReportType = ReportType.DailySummary;
        [ObservableProperty] private ReportFormat _newReportFormat = ReportFormat.PDF;
        [ObservableProperty] private ReportSchedule _newReportSchedule = ReportSchedule.None;

        // Enum collections for ComboBoxes
        public ReportType[]     AllReportTypes     => (ReportType[])Enum.GetValues(typeof(ReportType));
        public ReportFormat[]   AllReportFormats   => (ReportFormat[])Enum.GetValues(typeof(ReportFormat));
        public ReportSchedule[] AllReportSchedules => (ReportSchedule[])Enum.GetValues(typeof(ReportSchedule));

        // ── Commands ──────────────────────────────────────────────────

        [RelayCommand]
        private void LoadDefinitions()
        {
            ReportDefinitions.Clear();
            foreach (var d in ScheduledReportManager.Instance.GetAll())
                ReportDefinitions.Add(d);
        }

        [RelayCommand]
        private async Task GenerateNowAsync()
        {
            if (SelectedDefinition == null) return;
            IsGenerating = true;
            StatusText   = "\u062c\u0627\u0631\u064d \u0627\u0644\u0625\u0646\u0634\u0627\u0621...";
            try
            {
                var result = await ScheduledReportManager.Instance.RunReportAsync(SelectedDefinition);
                if (result.Success)
                {
                    LastGeneratedFilePath = result.FilePath ?? "";
                    StatusText = $"\u062a\u0645 \u0627\u0644\u0625\u0646\u0634\u0627\u0621 \u2705 \u2014 {Path.GetFileName(result.FilePath)}";
                    var open = MessageBox.Show(
                        $"\u062a\u0645 \u0625\u0646\u0634\u0627\u0621 \u0627\u0644\u062a\u0642\u0631\u064a\u0631:\n{result.FilePath}\n\n\u0647\u0644 \u062a\u0631\u064a\u062f \u0641\u062a\u062d\u0647 \u0627\u0644\u0622\u0646\u061f",
                        "\u062a\u0642\u0631\u064a\u0631 \u062c\u0627\u0647\u0632", MessageBoxButton.YesNo, MessageBoxImage.Information);
                    if (open == MessageBoxResult.Yes && result.FilePath != null)
                        Process.Start(new ProcessStartInfo(result.FilePath) { UseShellExecute = true });
                }
                else
                {
                    StatusText = $"\u062e\u0637\u0623: {result.ErrorMessage}";
                    MessageBox.Show($"\u0641\u0634\u0644 \u0625\u0646\u0634\u0627\u0621 \u0627\u0644\u062a\u0642\u0631\u064a\u0631:\n{result.ErrorMessage}", "\u062e\u0637\u0623",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                StatusText = $"\u062e\u0637\u0623 \u063a\u064a\u0631 \u0645\u062a\u0648\u0642\u0639: {ex.Message}";
                MessageBox.Show(ex.Message, "\u062e\u0637\u0623", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { IsGenerating = false; }
        }

        [RelayCommand]
        private void AddReportDefinition()
        {
            if (string.IsNullOrWhiteSpace(NewReportName))
            {
                MessageBox.Show("\u064a\u0631\u062c\u0649 \u0625\u062f\u062e\u0627\u0644 \u0627\u0633\u0645 \u0627\u0644\u062a\u0642\u0631\u064a\u0631", "\u062a\u062d\u0642\u0642", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var def = new ReportDefinition
            {
                Name     = NewReportName,
                Type     = NewReportType,
                Format   = NewReportFormat,
                Schedule = NewReportSchedule,
                FromDate = DateTime.Today,
                ToDate   = DateTime.Today
            };
            ScheduledReportManager.Instance.AddDefinition(def);
            NewReportName = "";
            LoadDefinitions();
        }

        [RelayCommand]
        private void DeleteSelectedDefinition()
        {
            if (SelectedDefinition == null) return;
            var r = MessageBox.Show(
                $"\u0647\u0644 \u062a\u0631\u064a\u062f \u062d\u0630\u0641 \u062a\u0642\u0631\u064a\u0631 '{SelectedDefinition.Name}'\u061f",
                "\u062d\u0630\u0641 \u062a\u0642\u0631\u064a\u0631", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
            ScheduledReportManager.Instance.DeleteDefinition(SelectedDefinition.Id);
            SelectedDefinition = null;
            LoadDefinitions();
        }

        [RelayCommand]
        private void OpenOutputFolder()
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Apex", "Reports", "Output");
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
        }

        [RelayCommand]
        private async Task GenerateTodayQuickReportAsync()
        {
            IsGenerating = true;
            StatusText   = "\u062c\u0627\u0631\u064d \u0625\u0646\u0634\u0627\u0621 \u062a\u0642\u0631\u064a\u0631 \u0627\u0644\u064a\u0648\u0645...";
            try
            {
                var def = new ReportDefinition
                {
                    Name     = $"\u062a\u0642\u0631\u064a\u0631 \u0627\u0644\u064a\u0648\u0645 \u2014 {DateTime.Today:dd/MM/yyyy}",
                    Type     = ReportType.DailySummary,
                    Format   = ReportFormat.PDF,
                    FromDate = DateTime.Today,
                    ToDate   = DateTime.Today
                };
                var result = await ScheduledReportManager.Instance.RunReportAsync(def);
                if (result.Success && result.FilePath != null)
                {
                    StatusText = "\u062a\u0645 \u2705";
                    Process.Start(new ProcessStartInfo(result.FilePath) { UseShellExecute = true });
                }
                else
                {
                    StatusText = $"\u062e\u0637\u0623: {result.ErrorMessage}";
                }
            }
            finally { IsGenerating = false; }
        }

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            ScheduledReportManager.Instance.EnsureDefaultSchedules();
            ScheduledReportManager.Instance.Start();
            LoadDefinitions();
        }
    }
}
