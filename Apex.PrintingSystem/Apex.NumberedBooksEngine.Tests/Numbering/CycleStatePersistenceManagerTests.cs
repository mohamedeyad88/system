using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Apex.Core.Enums;
using Apex.Core.Models;
using Apex.NumberedBooksEngine.Models;
using Apex.Services.Numbering;
using Xunit;

namespace Apex.NumberedBooksEngine.Tests.Numbering
{
    /// <summary>
    /// Unit tests for CycleStatePersistenceManager.
    /// Tests state saving, loading, deletion, and serialization.
    /// </summary>
    public class CycleStatePersistenceManagerTests : IDisposable
    {
        private readonly string _testStateDirectory;
        private readonly CycleStatePersistenceManager _manager;

        public CycleStatePersistenceManagerTests()
        {
            _testStateDirectory = Path.Combine(Path.GetTempPath(), $"CycleStateTests_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testStateDirectory);
            _manager = new CycleStatePersistenceManager(_testStateDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testStateDirectory))
            {
                Directory.Delete(_testStateDirectory, recursive: true);
            }
        }

        [Fact]
        public async Task TestSaveStateAsync_CreatesJsonFile()
        {
            // Arrange
            var jobId = Guid.NewGuid().ToString();
            var printerName = "TestPrinter";
            var templatePath = "test-template.pdf";
            var slots = new List<SlotSpec>
            {
                new SlotSpec("slot1", 0.1f, 0.1f, 0.2f, 0.1f, "Arial", 12f, "#000000", TextAlign.Center, 0f, null)
            };
            var startNumber = 1L;
            var totalNumbers = 10L;
            var copiesPerPage = 2;
            var trayMapping = new Dictionary<int, int>
            {
                { 0, (int)PaperSourceKind.Upper },
                { 1, (int)PaperSourceKind.Lower }
            };
            var dpi = 300;
            var cycles = CreateTestCycles(3);

            // Act
            await _manager.SaveStateAsync(
                jobId, printerName, templatePath, slots, startNumber, totalNumbers,
                copiesPerPage, trayMapping, dpi, cycles);

            // Assert
            var filePath = Path.Combine(_testStateDirectory, $"{jobId}.cycle-state.json");
            Assert.True(File.Exists(filePath));
        }

        [Fact]
        public async Task TestLoadStateAsync_DeserializesCorrectly()
        {
            // Arrange
            var jobId = Guid.NewGuid().ToString();
            var printerName = "TestPrinter";
            var templatePath = "test-template.pdf";
            var slots = new List<SlotSpec>
            {
                new SlotSpec("slot1", 0.1f, 0.1f, 0.2f, 0.1f, "Arial", 12f, "#000000", TextAlign.Center, 0f, null)
            };
            var startNumber = 1L;
            var totalNumbers = 10L;
            var copiesPerPage = 2;
            var trayMapping = new Dictionary<int, int>
            {
                { 0, (int)PaperSourceKind.Upper },
                { 1, (int)PaperSourceKind.Lower }
            };
            var dpi = 300;
            var cycles = CreateTestCycles(3);

            await _manager.SaveStateAsync(
                jobId, printerName, templatePath, slots, startNumber, totalNumbers,
                copiesPerPage, trayMapping, dpi, cycles);

            // Act
            var loaded = await _manager.LoadStateAsync(jobId);

            // Assert
            Assert.NotNull(loaded);
            Assert.Equal(jobId, loaded.JobId);
            Assert.Equal(printerName, loaded.PrinterName);
            Assert.Equal(templatePath, loaded.TemplatePath);
            Assert.Equal(startNumber, loaded.StartNumber);
            Assert.Equal(totalNumbers, loaded.TotalNumbers);
            Assert.Equal(copiesPerPage, loaded.CopiesPerPage);
            Assert.Equal(dpi, loaded.Dpi);
            Assert.Equal(3, loaded.Cycles.Count);
        }

        [Fact]
        public async Task TestLoadStateAsync_ReturnsNull_WhenFileNotExists()
        {
            // Act
            var loaded = await _manager.LoadStateAsync("non-existent-id");

            // Assert
            Assert.Null(loaded);
        }

        [Fact]
        public void TestDeleteState_RemovesFile()
        {
            // Arrange
            var jobId = Guid.NewGuid().ToString();
            var filePath = Path.Combine(_testStateDirectory, $"{jobId}.cycle-state.json");
            File.WriteAllText(filePath, "{}");

            // Act
            _manager.DeleteState(jobId);

            // Assert
            Assert.False(File.Exists(filePath));
        }

        [Fact]
        public void TestDeleteState_NoError_WhenFileNotExists()
        {
            // Act & Assert - should not throw
            _manager.DeleteState("non-existent-id");
        }

        [Fact]
        public async Task TestGetPendingStatesAsync_ReturnsAllPendingStates()
        {
            // Arrange
            var jobId1 = Guid.NewGuid().ToString();
            var jobId2 = Guid.NewGuid().ToString();
            var jobId3 = Guid.NewGuid().ToString();

            var slots = new List<SlotSpec>
            {
                new SlotSpec("slot1", 0.1f, 0.1f, 0.2f, 0.1f, "Arial", 12f, "#000000", TextAlign.Center, 0f, null)
            };
            var trayMapping = new Dictionary<int, int> { { 0, (int)PaperSourceKind.Upper } };

            await _manager.SaveStateAsync(
                jobId1, "Printer1", "template1.pdf", slots, 1L, 10L, 1, trayMapping, 300, CreateTestCycles(2));
            await _manager.SaveStateAsync(
                jobId2, "Printer2", "template2.pdf", slots, 1L, 20L, 1, trayMapping, 300, CreateTestCycles(3));
            await _manager.SaveStateAsync(
                jobId3, "Printer3", "template3.pdf", slots, 1L, 30L, 1, trayMapping, 300, CreateTestCycles(4));

            // Act
            var pending = await _manager.GetPendingStatesAsync();

            // Assert
            Assert.Equal(3, pending.Count);
            Assert.Contains(pending, s => s.JobId == jobId1);
            Assert.Contains(pending, s => s.JobId == jobId2);
            Assert.Contains(pending, s => s.JobId == jobId3);
        }

        [Fact]
        public async Task TestSaveStateAsync_PreservesCycleStates()
        {
            // Arrange
            var jobId = Guid.NewGuid().ToString();
            var cycles = new List<CycleJob>
            {
                CreateCycleJob(1, 1L, CycleStatus.CompletedPhysical, "parent-id"),
                CreateCycleJob(2, 2L, CycleStatus.Failed, null, "error-message"),
                CreateCycleJob(3, 3L, CycleStatus.Pending, null)
            };

            var slots = new List<SlotSpec>
            {
                new SlotSpec("slot1", 0.1f, 0.1f, 0.2f, 0.1f, "Arial", 12f, "#000000", TextAlign.Center, 0f, null)
            };
            var trayMapping = new Dictionary<int, int> { { 0, (int)PaperSourceKind.Upper } };

            // Act
            await _manager.SaveStateAsync(
                jobId, "Printer", "template.pdf", slots, 1L, 3L, 1, trayMapping, 300, cycles);

            var loaded = await _manager.LoadStateAsync(jobId);

            // Assert
            Assert.NotNull(loaded);
            Assert.Equal(3, loaded.Cycles.Count);
            Assert.Equal(CycleStatus.CompletedPhysical, loaded.Cycles[0].Status);
            Assert.Equal(CycleStatus.Failed, loaded.Cycles[1].Status);
            Assert.Equal("error-message", loaded.Cycles[1].ErrorMessage);
            Assert.Equal(CycleStatus.Pending, loaded.Cycles[2].Status);
        }

        [Fact]
        public async Task TestSaveStateAsync_PreservesDependencies()
        {
            // Arrange
            var jobId = Guid.NewGuid().ToString();
            var parent = CreateCycleJob(1, 1L, CycleStatus.CompletedPhysical, null);
            var child = CreateCycleJob(2, 2L, CycleStatus.Pending, parent.JobId);
            var cycles = new List<CycleJob> { parent, child };

            var slots = new List<SlotSpec>
            {
                new SlotSpec("slot1", 0.1f, 0.1f, 0.2f, 0.1f, "Arial", 12f, "#000000", TextAlign.Center, 0f, null)
            };
            var trayMapping = new Dictionary<int, int> { { 0, (int)PaperSourceKind.Upper } };

            // Act
            await _manager.SaveStateAsync(
                jobId, "Printer", "template.pdf", slots, 1L, 2L, 1, trayMapping, 300, cycles);

            var loaded = await _manager.LoadStateAsync(jobId);

            // Assert
            Assert.NotNull(loaded);
            var loadedChild = loaded.Cycles.FirstOrDefault(c => c.CycleNumber == 2);
            Assert.NotNull(loadedChild);
            Assert.Equal(parent.JobId, loadedChild.DependsOnJobId);
        }

        [Fact]
        public async Task TestSaveStateAsync_PreservesTimestamps()
        {
            // Arrange
            var jobId = Guid.NewGuid().ToString();
            var startedAt = DateTime.UtcNow.AddMinutes(-10);
            var completedAt = DateTime.UtcNow.AddMinutes(-5);
            var cycle = CreateCycleJob(1, 1L, CycleStatus.CompletedPhysical, null);
            cycle.StartedAtUtc = startedAt;
            cycle.CompletedAtUtc = completedAt;

            var slots = new List<SlotSpec>
            {
                new SlotSpec("slot1", 0.1f, 0.1f, 0.2f, 0.1f, "Arial", 12f, "#000000", TextAlign.Center, 0f, null)
            };
            var trayMapping = new Dictionary<int, int> { { 0, (int)PaperSourceKind.Upper } };

            // Act
            await _manager.SaveStateAsync(
                jobId, "Printer", "template.pdf", slots, 1L, 1L, 1, trayMapping, 300, new[] { cycle });

            var loaded = await _manager.LoadStateAsync(jobId);

            // Assert
            Assert.NotNull(loaded);
            var loadedCycle = loaded.Cycles[0];
            Assert.NotNull(loadedCycle.StartedAtUtc);
            Assert.NotNull(loadedCycle.CompletedAtUtc);
            // Allow small time difference due to serialization
            Assert.True(Math.Abs((loadedCycle.StartedAtUtc!.Value - startedAt).TotalSeconds) < 1);
            Assert.True(Math.Abs((loadedCycle.CompletedAtUtc!.Value - completedAt).TotalSeconds) < 1);
        }

        [Fact]
        public async Task TestSaveStateAsync_PreservesLastError()
        {
            // Arrange
            var jobId = Guid.NewGuid().ToString();
            var lastError = "Test error message";
            var cycles = CreateTestCycles(2);

            var slots = new List<SlotSpec>
            {
                new SlotSpec("slot1", 0.1f, 0.1f, 0.2f, 0.1f, "Arial", 12f, "#000000", TextAlign.Center, 0f, null)
            };
            var trayMapping = new Dictionary<int, int> { { 0, (int)PaperSourceKind.Upper } };

            // Act
            await _manager.SaveStateAsync(
                jobId, "Printer", "template.pdf", slots, 1L, 2L, 1, trayMapping, 300, cycles, lastError);

            var loaded = await _manager.LoadStateAsync(jobId);

            // Assert
            Assert.NotNull(loaded);
            Assert.Equal(lastError, loaded.LastError);
        }

        private List<CycleJob> CreateTestCycles(int count)
        {
            var cycles = new List<CycleJob>();
            CycleJob? previous = null;

            for (int i = 0; i < count; i++)
            {
                var cycle = CreateCycleJob(i + 1, i + 1L, CycleStatus.Pending, previous?.JobId);
                cycles.Add(cycle);
                previous = cycle;
            }

            return cycles;
        }

        private CycleJob CreateCycleJob(int cycleNumber, long number, CycleStatus status, string? dependsOn, string? errorMessage = null)
        {
            var job = new CycleJob
            {
                CycleNumber = cycleNumber,
                StartNumber = number,
                EndNumber = number,
                CopiesPerPage = 1,
                TrayMapping = new Dictionary<int, int> { { 0, (int)PaperSourceKind.Upper } },
                DependsOnJobId = dependsOn,
                Status = status,
                ErrorMessage = errorMessage,
                Pages = new List<CyclePage>
                {
                    new CyclePage
                    {
                        Number = number,
                        Type = CopyType.Original,
                        Tray = (int)PaperSourceKind.Upper,
                        PageIndex = 0,
                        Slots = new List<SlotSpec>
                        {
                            new SlotSpec("slot1", 0.1f, 0.1f, 0.2f, 0.1f, "Arial", 12f, "#000000", TextAlign.Center, 0f, null)
                        }
                    }
                }
            };

            if (status == CycleStatus.Printing || status == CycleStatus.CompletedPhysical)
            {
                job.StartedAtUtc = DateTime.UtcNow;
            }

            if (status.IsTerminal())
            {
                job.CompletedAtUtc = DateTime.UtcNow;
            }

            return job;
        }
    }
}

