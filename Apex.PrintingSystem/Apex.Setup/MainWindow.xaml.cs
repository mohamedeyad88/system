using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace Apex.Setup
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new InstallerViewModel(this);
        }
    }

    public class InstallerViewModel : INotifyPropertyChanged
    {
        private readonly Window _window;
        private string _installPath = string.Empty;
        private string _statusText = "Ready to Install";
        private double _progressValue = 0;
        private Visibility _welcomeVisibility = Visibility.Visible;
        private Visibility _progressVisibility = Visibility.Collapsed;
        private Visibility _completeVisibility = Visibility.Collapsed;
        private Visibility _installButtonVisibility = Visibility.Visible;
        private Visibility _closeButtonVisibility = Visibility.Collapsed;
        private bool _launchApp = true;

        public event PropertyChangedEventHandler? PropertyChanged;

        public InstallerViewModel(Window window)
        {
            _window = window;
            InstallPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApexPrintingSystem");
            InstallCommand = new RelayCommand(Install);
            CloseCommand = new RelayCommand(Close);
        }

        public string InstallPath
        {
            get => _installPath;
            set => SetProperty(ref _installPath, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public double ProgressValue
        {
            get => _progressValue;
            set => SetProperty(ref _progressValue, value);
        }

        public Visibility WelcomeVisibility
        {
            get => _welcomeVisibility;
            set => SetProperty(ref _welcomeVisibility, value);
        }

        public Visibility ProgressVisibility
        {
            get => _progressVisibility;
            set => SetProperty(ref _progressVisibility, value);
        }

        public Visibility CompleteVisibility
        {
            get => _completeVisibility;
            set => SetProperty(ref _completeVisibility, value);
        }

        public Visibility InstallButtonVisibility
        {
            get => _installButtonVisibility;
            set => SetProperty(ref _installButtonVisibility, value);
        }

        public Visibility CloseButtonVisibility
        {
            get => _closeButtonVisibility;
            set => SetProperty(ref _closeButtonVisibility, value);
        }

        public bool LaunchApp
        {
            get => _launchApp;
            set => SetProperty(ref _launchApp, value);
        }

        public ICommand InstallCommand { get; }
        public ICommand CloseCommand { get; }

        private async void Install()
        {
            WelcomeVisibility = Visibility.Collapsed;
            ProgressVisibility = Visibility.Visible;
            InstallButtonVisibility = Visibility.Collapsed;

            try
            {
                await Task.Run(() => PerformInstallation());

                ProgressVisibility = Visibility.Collapsed;
                CompleteVisibility = Visibility.Visible;
                CloseButtonVisibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Installation Failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _window.Close();
            }
        }

        private void PerformInstallation()
        {
            UpdateStatus("Preparing installation...", 10);

            if (Directory.Exists(InstallPath))
            {
                UpdateStatus("Cleaning up old files...", 20);
                try { Directory.Delete(InstallPath, true); } catch { }
            }
            Directory.CreateDirectory(InstallPath);

            UpdateStatus("Extracting files...", 40);

            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Apex.Setup.publish.zip"))
            {
                if (stream == null) throw new Exception("Installer payload not found.");

                string zipPath = Path.Combine(InstallPath, "publish.zip");
                using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write))
                {
                    stream.CopyTo(fileStream);
                }

                UpdateStatus("Unpacking application...", 60);
                ZipFile.ExtractToDirectory(zipPath, InstallPath);
                File.Delete(zipPath);
            }

            UpdateStatus("Creating shortcuts...", 80);
            CreateShortcut("Apex Printing System", Path.Combine(InstallPath, "Apex.UI.exe"));

            UpdateStatus("Finalizing...", 100);
        }

        private void CreateShortcut(string shortcutName, string targetPath)
        {
            try
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string shortcutPath = Path.Combine(desktopPath, shortcutName + ".lnk");
                string startMenuPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", shortcutName + ".lnk");

                string powershellCommand = $"$s=(New-Object -COM WScript.Shell).CreateShortcut('{shortcutPath}');$s.TargetPath='{targetPath}';$s.WorkingDirectory='{Path.GetDirectoryName(targetPath)}';$s.IconLocation='{targetPath}';$s.Save()";

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-NoProfile -Command \"{powershellCommand}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi)?.WaitForExit();

                File.Copy(shortcutPath, startMenuPath, true);
            }
            catch { /* Ignore shortcut errors */ }
        }

        private void UpdateStatus(string text, double progress)
        {
            StatusText = text;
            ProgressValue = progress;
        }

        private void Close()
        {
            if (LaunchApp)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(Path.Combine(InstallPath, "Apex.UI.exe")) { UseShellExecute = true, WorkingDirectory = InstallPath });
                }
                catch { }
            }
            _window.Close();
        }

        protected void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute();

        public void Execute(object? parameter) => _execute();
    }
}