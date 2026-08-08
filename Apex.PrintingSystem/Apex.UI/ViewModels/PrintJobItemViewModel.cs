using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.UI.ViewModels
{
    /// <summary>
    /// Represents a single print job row in a monitoring list.
    /// Moved from DistributionViewModel after that module was removed.
    /// </summary>
    public partial class PrintJobItemViewModel : ObservableObject
    {
        [ObservableProperty] private string _jobId = "";
        [ObservableProperty] private string _fileName = "";
        [ObservableProperty] private string _printerName = "";
        [ObservableProperty] private string _status = "";
        [ObservableProperty] private int _progress;
        [ObservableProperty] private string _startTime = "";
        [ObservableProperty] private bool _isSuccess;
        [ObservableProperty] private bool _isFailed;
        [ObservableProperty] private bool _isRunning;
    }
}
