using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Apex.Core.Enums;
using Apex.Core.Models;
using Apex.NumberedBooksEngine.Models;

namespace Apex.Services.Numbering
{
    /// <summary>
    /// State record for cycle-based printing job persistence.
    /// </summary>
    public record CyclePrintState(
        string JobId,
        string PrinterName,
        string TemplatePath,
        IReadOnlyList<SlotSpec> Slots,
        long StartNumber,
        long TotalNumbers,
        int CopiesPerPage,
        Dictionary<int, int> TrayMapping,
        int Dpi,
        DateTime CreatedAtUtc,
        DateTime? PausedAtUtc,
        List<CycleJobState> Cycles,
        string? LastError
    );

    /// <summary>
    /// Serializable state for a single cycle.
    /// </summary>
    public record CycleJobState(
        string JobId,
        int CycleNumber,
        long StartNumber,
        long EndNumber,
        CycleStatus Status,
        string? DependsOnJobId,
        DateTime? StartedAtUtc,
        DateTime? CompletedAtUtc,
        string? ErrorMessage
    );

    /// <summary>
    /// Manages persistence of cycle-based printing state for resume capability.
    /// Saves state to JSON file on Pause/Error, allows resuming from saved state.
    /// </summary>
    public class CycleStatePersistenceManager
    {
        private readonly string _stateDirectory;

        public CycleStatePersistenceManager(string? stateDirectory = null)
        {
            _stateDirectory = stateDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ApexPrintingSystem", "CycleStates");

            Directory.CreateDirectory(_stateDirectory);
        }

        /// <summary>
        /// Saves the current state of all cycles for a print job.
        /// </summary>
        public async Task SaveStateAsync(
            string jobId,
            string printerName,
            string templatePath,
            IReadOnlyList<SlotSpec> slots,
            long startNumber,
            long totalNumbers,
            int copiesPerPage,
            Dictionary<int, int> trayMapping,
            int dpi,
            IEnumerable<CycleJob> cycles,
            string? lastError = null)
        {
            var cycleStates = cycles.Select(c => new CycleJobState(
                JobId: c.JobId,
                CycleNumber: c.CycleNumber,
                StartNumber: c.StartNumber,
                EndNumber: c.EndNumber,
                Status: c.Status,
                DependsOnJobId: c.DependsOnJobId,
                StartedAtUtc: c.StartedAtUtc,
                CompletedAtUtc: c.CompletedAtUtc,
                ErrorMessage: c.ErrorMessage
            )).ToList();

            var state = new CyclePrintState(
                JobId: jobId,
                PrinterName: printerName,
                TemplatePath: templatePath,
                Slots: slots,
                StartNumber: startNumber,
                TotalNumbers: totalNumbers,
                CopiesPerPage: copiesPerPage,
                TrayMapping: trayMapping,
                Dpi: dpi,
                CreatedAtUtc: DateTime.UtcNow,
                PausedAtUtc: DateTime.UtcNow,
                Cycles: cycleStates,
                LastError: lastError
            );

            var filePath = GetStatePath(jobId);
            var tempFilePath = filePath + ".tmp";
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            };

            // Atomic write: write to temp file first, then rename
            var json = JsonSerializer.Serialize(state, options);
            await File.WriteAllTextAsync(tempFilePath, json);

            // Atomic rename (overwrites existing file if any)
            if (File.Exists(filePath))
                File.Delete(filePath);
            File.Move(tempFilePath, filePath);
        }

        /// <summary>
        /// Loads saved state for a job, if it exists.
        /// </summary>
        public async Task<CyclePrintState?> LoadStateAsync(string jobId)
        {
            var filePath = GetStatePath(jobId);

            if (!File.Exists(filePath))
                return null;

            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                };

                return JsonSerializer.Deserialize<CyclePrintState>(json, options);
            }
            catch (Exception ex)
            {
                // Log error if needed - for now just return null
                System.Diagnostics.Debug.WriteLine($"Failed to load state for {jobId}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Deletes saved state for a completed job.
        /// </summary>
        public void DeleteState(string jobId)
        {
            var filePath = GetStatePath(jobId);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        /// <summary>
        /// Gets all pending states (for recovery on app restart).
        /// </summary>
        public async Task<List<CyclePrintState>> GetPendingStatesAsync()
        {
            var files = Directory.GetFiles(_stateDirectory, "*.cycle-state.json");
            var states = new List<CyclePrintState>();

            foreach (var file in files)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var options = new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                    };

                    var state = JsonSerializer.Deserialize<CyclePrintState>(json, options);
                    if (state != null)
                        states.Add(state);
                }
                catch (Exception ex)
                {
                    // Ignore corrupt state files, but log for debugging
                    System.Diagnostics.Debug.WriteLine($"Failed to load state file {file}: {ex.Message}");
                }
            }

            return states;
        }

        private string GetStatePath(string jobId)
        {
            return Path.Combine(_stateDirectory, $"{jobId}.cycle-state.json");
        }
    }
}

