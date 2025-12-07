using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Apex.Services.Helpers;
using Apex.Core.Interfaces;
using System.IO;
using System.Linq;

namespace Apex.UI.Services.Printing
{
    public class DistributionPrintService : IPrintModule
    {
        private readonly IPrintEngine _printEngine;

        public DistributionPrintService(IPrintEngine printEngine)
        {
            _printEngine = printEngine;
        }

        public string ModuleName => "Single-File Multi-Printer";
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

        public async Task DistributeJobAsync(string filePath, List<string> targetPrinters)
        {
            _cancelRequested = false;
            int total = targetPrinters.Count;
            int completed = 0;
            
            OnStatusUpdate?.Invoke(this, $"Distributing {Path.GetFileName(filePath)} to {total} printers...");

            var tasks = new List<Task>();

            foreach (var printer in targetPrinters)
            {
                tasks.Add(Task.Run(async () =>
                {
                    if (_cancelRequested) return;

                    bool success = await _printEngine.PrintAsync(printer, filePath);
                    
                    lock (this)
                    {
                        completed++;
                        OnProgressUpdate?.Invoke(this, (int)((double)completed / total * 100));
                        OnStatusUpdate?.Invoke(this, success ? $"Sent to {printer}" : $"Failed on {printer}");
                    }
                }));
            }

            await Task.WhenAll(tasks);
            
            OnStatusUpdate?.Invoke(this, "Distribution Complete");
            IsRunning = false;
        }
    }
}
