using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Apex.NumberedBooksEngine.Core
{
    /// <summary>
    /// Checkpoint record for job resume.
    /// </summary>
    public record CheckpointRecord(
        string JobId,
        long LastPrintedNumber,
        DateTime Timestamp,
        long PagesPrinted,
        string? SpoolerJobId
    );

    /// <summary>
    /// Manages job checkpoints for resume capability.
    /// Persists checkpoint every N pages to enable recovery after failures.
    /// </summary>
    public class CheckpointManager
    {
        private readonly string _checkpointDirectory;
        private readonly int _checkpointInterval;

        public CheckpointManager(string? checkpointDirectory = null, int checkpointInterval = 500)
        {
            // ProgramData, not Temp. A checkpoint is the only record of where a 20,000-sheet
            // run stopped, and Windows empties Temp whenever it feels short of disk — the
            // one place recovery data must not live. Per-machine, like the number register,
            // so it survives user profiles and reinstalls.
            _checkpointDirectory = checkpointDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "ApexPrintingSystem", "checkpoints");
            _checkpointInterval = checkpointInterval;

            Directory.CreateDirectory(_checkpointDirectory);
        }

        /// <summary>
        /// Saves a checkpoint if the page count is at a checkpoint interval.
        /// </summary>
        public async Task SaveCheckpointIfNeeded(string jobId, long lastNumber, long pagesPrinted, string? spoolerJobId = null)
        {
            if (pagesPrinted % _checkpointInterval == 0)
            {
                await SaveCheckpointAsync(jobId, lastNumber, pagesPrinted, spoolerJobId);
            }
        }

        /// <summary>
        /// Forces a checkpoint save.
        /// </summary>
        public async Task SaveCheckpointAsync(string jobId, long lastNumber, long pagesPrinted, string? spoolerJobId = null)
        {
            var checkpoint = new CheckpointRecord(
                JobId: jobId,
                LastPrintedNumber: lastNumber,
                Timestamp: DateTime.UtcNow,
                PagesPrinted: pagesPrinted,
                SpoolerJobId: spoolerJobId
            );

            var filePath = GetCheckpointPath(jobId);
            var json = JsonSerializer.Serialize(checkpoint, new JsonSerializerOptions { WriteIndented = true });

            await File.WriteAllTextAsync(filePath, json);
        }

        /// <summary>
        /// Loads the checkpoint for a job, if it exists.
        /// </summary>
        public async Task<CheckpointRecord?> LoadCheckpointAsync(string jobId)
        {
            var filePath = GetCheckpointPath(jobId);

            if (!File.Exists(filePath))
                return null;

            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                return JsonSerializer.Deserialize<CheckpointRecord>(json);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Deletes the checkpoint for a completed job.
        /// </summary>
        public void DeleteCheckpoint(string jobId)
        {
            var filePath = GetCheckpointPath(jobId);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        /// <summary>
        /// Gets all pending checkpoints (for recovery on app restart).
        /// </summary>
        public async Task<CheckpointRecord[]> GetPendingCheckpointsAsync()
        {
            var files = Directory.GetFiles(_checkpointDirectory, "*.checkpoint.json");
            var checkpoints = new System.Collections.Generic.List<CheckpointRecord>();

            foreach (var file in files)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var checkpoint = JsonSerializer.Deserialize<CheckpointRecord>(json);
                    if (checkpoint != null)
                        checkpoints.Add(checkpoint);
                }
                catch
                {
                    // Ignore corrupt checkpoint files
                }
            }

            return checkpoints.ToArray();
        }

        /// <summary>
        /// Gets the resume start number for a job.
        /// </summary>
        public async Task<long?> GetResumeStartNumberAsync(string jobId)
        {
            var checkpoint = await LoadCheckpointAsync(jobId);
            return checkpoint?.LastPrintedNumber + 1;
        }

        private string GetCheckpointPath(string jobId)
        {
            return Path.Combine(_checkpointDirectory, $"{jobId}.checkpoint.json");
        }
    }
}
