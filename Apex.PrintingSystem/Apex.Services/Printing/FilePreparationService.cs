using Apex.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Apex.Services.Printing
{
    public class FilePreparationService
    {
        public async Task<List<BatchJob>> PrepareFilesAsync(IEnumerable<string> filePaths)
        {
            return await Task.Run(() =>
            {
                var list = new List<BatchJob>();
                foreach (var path in filePaths)
                {
                    if (!File.Exists(path)) continue;

                    var fi = new FileInfo(path);
                    var job = new BatchJob
                    {
                        FilePath = path,
                        FileType = fi.Extension.ToUpper(),
                        FileSize = $"{fi.Length / 1024} KB",
                        Status = "Ready"
                    };

                    // Optional: Heavy logic to count pages for PDF/Word could go here
                    // For now we just validate existence and basic info

                    list.Add(job);
                }
                return list;
            });
        }
    }
}
