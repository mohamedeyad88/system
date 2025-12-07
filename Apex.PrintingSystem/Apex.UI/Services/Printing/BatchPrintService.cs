using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Apex.Services.Helpers;
using Apex.Core.Interfaces;
using System.IO;
using System.Linq;

namespace Apex.UI.Services.Printing
{
    public class BatchPrintService : IPrintModule
    {
        private readonly IPrintEngine _printEngine;

        public BatchPrintService(IPrintEngine printEngine)
        {
            _printEngine = printEngine;
        }

        public string ModuleName => "Multi-File Single-Printer";
        public bool IsRunning { get; private set; }

        public event EventHandler<string> OnStatusUpdate;
        public event EventHandler<int> OnProgressUpdate;

        private bool _cancelRequested;

        public Task StartAsync()
        {
            IsRunning = true;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            IsRunning = false;
            _cancelRequested = true;
            return Task.CompletedTask;
        }

        public async Task PrintBatchAsync(string printerName, List<string> filePaths)
        {
            _cancelRequested = false;
            int total = filePaths.Count;
            int current = 0;

            foreach (var file in filePaths)
            {
                if (_cancelRequested) break;

                OnStatusUpdate?.Invoke(this, $"Printing {Path.GetFileName(file)}...");
                
                bool success = await _printEngine.PrintAsync(printerName, file);

                if (success)
                {
                    OnStatusUpdate?.Invoke(this, $"Completed {Path.GetFileName(file)}");
                }
                else
                {
                    OnStatusUpdate?.Invoke(this, $"Failed {Path.GetFileName(file)}");
                }

                current++;
                OnProgressUpdate?.Invoke(this, (int)((double)current / total * 100));
                
                // Configurable delay could go here
                await Task.Delay(500); 
            }

            OnStatusUpdate?.Invoke(this, "Batch Complete");
            IsRunning = false;
        }
    }
}
