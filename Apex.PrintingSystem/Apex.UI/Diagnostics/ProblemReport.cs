using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Apex.UI.ViewModels;

namespace Apex.UI.Diagnostics
{
    /// <summary>
    /// "Report a problem": one zip a shop can send over WhatsApp.
    ///
    /// <para>Without it an operator describes a fault in words and support guesses — which
    /// version, which printer, what the log said. The zip carries the logs, the version,
    /// the device id, what Windows recorded about crashes, and the number register (the
    /// only evidence of which numbers really reached paper).</para>
    ///
    /// <para>Deliberately NOT included: the database, designs, customer files and the
    /// licence. Files are picked by an allow-list, never by sweeping folders.</para>
    /// </summary>
    public static class ProblemReport
    {
        private const long MaxFileBytes = 50L * 1024 * 1024;
        private static readonly TimeSpan LogWindow = TimeSpan.FromDays(14);

        public static string LocalAppDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApexPrintingSystem");

        public static string ProgramDataDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ApexPrintingSystem");

        public static string DefaultFileName(DateTime now) => $"Apex-Report-{now:yyyyMMdd-HHmm}.zip";

        /// <summary>The files that go into a report, as (entry name in the zip, path on disk).</summary>
        public static IReadOnlyList<(string Entry, string Path)> CollectFiles(string localAppDir, string programDataDir, DateTime now)
        {
            var found = new List<(string, string)>();
            // "logs" (PrintLogger) and "Logs" (AppDiagnostics) are the same folder on Windows.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(string entry, string path)
            {
                try
                {
                    var file = new FileInfo(path);
                    if (!file.Exists || file.Length > MaxFileBytes || !seen.Add(file.FullName)) return;
                    found.Add((entry, file.FullName));
                }
                catch { }
            }

            void AddFolder(string dir, string entryDir, string pattern, bool recentOnly)
            {
                if (!Directory.Exists(dir)) return;
                foreach (var f in Directory.EnumerateFiles(dir, pattern, SearchOption.TopDirectoryOnly))
                {
                    if (recentOnly && now - File.GetLastWriteTime(f) > LogWindow) continue;
                    Add($"{entryDir}/{Path.GetFileName(f)}", f);
                }
            }

            AddFolder(localAppDir, "app", "*.log", recentOnly: true);
            AddFolder(localAppDir, "app", "*.txt", recentOnly: true);
            AddFolder(Path.Combine(localAppDir, "logs"), "logs", "*.log", recentOnly: true);
            AddFolder(Path.Combine(localAppDir, "logs"), "logs", "*.txt", recentOnly: true);
            Add("numbering/number-registry.jsonl", Path.Combine(programDataDir, "number-registry.jsonl"));
            AddFolder(Path.Combine(programDataDir, "checkpoints"), "numbering/checkpoints", "*", recentOnly: false);

            return found;
        }

        /// <summary>Writes the zip. Reads logs the logger still holds open.</summary>
        public static void Write(string zipPath, string summary, string windowsEvents,
            IEnumerable<(string Entry, string Path)> files)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(zipPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // Built beside the target and moved in at the end, so a failure halfway never
            // leaves a truncated zip that looks like a finished report.
            var partial = zipPath + ".partial";
            if (File.Exists(partial)) File.Delete(partial);

            using (var zip = ZipFile.Open(partial, ZipArchiveMode.Create))
            {
                WriteText(zip, "summary.txt", summary);
                WriteText(zip, "windows-events.txt", windowsEvents);

                foreach (var (entry, path) in files)
                {
                    try
                    {
                        using var source = new FileStream(path, FileMode.Open, FileAccess.Read,
                            FileShare.ReadWrite | FileShare.Delete);
                        using var target = zip.CreateEntry(entry, CompressionLevel.Optimal).Open();
                        source.CopyTo(target);
                    }
                    catch (Exception ex)
                    {
                        WriteText(zip, entry + ".unreadable.txt", ex.Message);
                    }
                }
            }

            File.Move(partial, zipPath, overwrite: true);
        }

        public static string BuildSummary(DateTime now, IReadOnlyList<(string Entry, string Path)> files)
        {
            var sb = new StringBuilder();
            void Line(string label, string value) => sb.AppendLine($"{label,-20}: {value}");

            sb.AppendLine("Apex Print OS — problem report");
            sb.AppendLine();
            Line("Created", now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
            Line("App version", Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "?");
            Line("Executable", Environment.ProcessPath ?? "?");
            Line("Windows", $"{RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture}");
            Line(".NET", RuntimeInformation.FrameworkDescription);
            Line("UI language", CultureInfo.CurrentUICulture.Name);
            Line("Device ID (licence)", Safe(() => Apex.Licensing.MachineIdentity.ToInfo(Apex.Licensing.LicenseManager.GetLicensedDeviceId()).DisplayId));
            Line("Device ID (current)", Safe(() => Apex.Licensing.LicenseManager.GetDeviceInfo().DisplayId));
            Line("Accepted device IDs", Safe(() => string.Join(", ", Apex.Licensing.MachineIdentity.GetCandidateDeviceIds())));
            Line("License", Safe(() =>
            {
                var r = Apex.Licensing.LicenseManager.Validate();
                return $"{r.Status} / {r.Type} / {r.DaysRemaining} days left";
            }));
            Line("Default printer", Safe(() =>
            {
                using var server = new System.Printing.LocalPrintServer();
                return server.DefaultPrintQueue?.FullName ?? "(none)";
            }));
            Line("Printers", Safe(() =>
            {
                using var server = new System.Printing.LocalPrintServer();
                var queues = server.GetPrintQueues(new[]
                {
                    System.Printing.EnumeratedPrintQueueTypes.Local,
                    System.Printing.EnumeratedPrintQueueTypes.Connections,
                });
                return string.Join(" | ", queues.Select(q => q.FullName));
            }));
            Line("System drive free", Safe(() =>
            {
                var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
                // Invariant: the report is read by support, not in the operator's locale.
                return (drive.AvailableFreeSpace / (1024 * 1024 * 1024.0)).ToString("F1", CultureInfo.InvariantCulture) + " GB";
            }));
            Line("App running for", Safe(() => (DateTime.Now - Process.GetCurrentProcess().StartTime).ToString(@"d\.hh\:mm\:ss")));

            sb.AppendLine();
            sb.AppendLine("Files:");
            foreach (var (entry, _) in files) sb.AppendLine("  " + entry);
            return sb.ToString();
        }

        /// <summary>Asks where to save, builds the report off the UI thread, then shows it.</summary>
        public static async Task SaveInteractiveAsync(Window? owner)
        {
            var now = DateTime.Now;
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = ViewModelBase.L("Report_DialogTitle"),
                Filter = "ZIP (*.zip)|*.zip",
                DefaultExt = ".zip",
                AddExtension = true,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                FileName = DefaultFileName(now),
            };
            var chosen = owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
            if (chosen != true) return;

            var path = dialog.FileName;
            var cursor = Mouse.OverrideCursor;
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                await Task.Run(() =>
                {
                    var files = CollectFiles(LocalAppDir, ProgramDataDir, now);
                    var events = WindowsCrashRecords.Format(WindowsCrashRecords.Since(DateTime.UtcNow - LogWindow, 30));
                    Write(path, BuildSummary(now, files), events, files);
                });
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = cursor;
                MessageBox.Show(string.Format(ViewModelBase.L("Report_Failed"), ex.Message),
                    ViewModelBase.L("Report_DialogTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            finally
            {
                Mouse.OverrideCursor = cursor;
            }

            try { Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { }
            MessageBox.Show(string.Format(ViewModelBase.L("Report_Saved"), path),
                ViewModelBase.L("Report_DialogTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static void WriteText(ZipArchive zip, string entry, string text)
        {
            using var writer = new StreamWriter(zip.CreateEntry(entry, CompressionLevel.Optimal).Open(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write(text);
        }

        private static string Safe(Func<string> read)
        {
            try { return read(); }
            catch (Exception ex) { return $"(unavailable: {ex.GetType().Name})"; }
        }
    }
}
