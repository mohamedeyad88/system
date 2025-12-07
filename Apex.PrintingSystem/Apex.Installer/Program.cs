using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace Apex.Installer
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("==========================================");
            Console.WriteLine("   Apex Printing System - Installer");
            Console.WriteLine("==========================================");
            Console.WriteLine();

            string installPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApexPrintingSystem");
            
            Console.WriteLine($"Installing to: {installPath}");
            Console.WriteLine("Press ENTER to continue or type a new path:");
            string input = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(input))
            {
                installPath = input;
            }

            try
            {
                if (Directory.Exists(installPath))
                {
                    Console.WriteLine("Cleaning up existing installation...");
                    Directory.Delete(installPath, true);
                }
                Directory.CreateDirectory(installPath);

                Console.WriteLine("Extracting application files...");
                // Extract the embedded zip
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Apex.Installer.publish.zip"))
                {
                    if (stream == null)
                    {
                        Console.WriteLine("Error: Embedded resource 'publish.zip' not found.");
                        return;
                    }

                    string zipPath = Path.Combine(installPath, "publish.zip");
                    using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write))
                    {
                        stream.CopyTo(fileStream);
                    }

                    // Unzip
                    ZipFile.ExtractToDirectory(zipPath, installPath);
                    File.Delete(zipPath);
                }

                Console.WriteLine("Creating Shortcut...");
                CreateShortcut("Apex Printing System", Path.Combine(installPath, "Apex.UI.exe"));

                Console.WriteLine("Installation Complete!");
                Console.WriteLine("Launching Application...");
                
                Process.Start(new ProcessStartInfo(Path.Combine(installPath, "Apex.UI.exe")) { UseShellExecute = true, WorkingDirectory = installPath });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.ReadLine();
            }
        }

        static void CreateShortcut(string shortcutName, string targetPath)
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

            // Copy to Start Menu
            try
            {
                File.Copy(shortcutPath, startMenuPath, true);
            }
            catch { /* Ignore if start menu copy fails */ }
        }
    }
}
