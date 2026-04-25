using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.Linq;
using Apex.Core.Enums;
using Apex.Core.Models;
using Apex.NumberedBooksEngine.Models;
using Apex.Services.Numbering;
using Xunit;

namespace Apex.NumberedBooksEngine.Tests.Numbering
{
    /// <summary>
    /// Unit tests for CycleOrchestrator.
    /// Tests cycle creation, dependency assignment, batch preparation, and tray verification.
    /// </summary>
    public class CycleOrchestratorTests
    {
        private readonly JobDependencyManager _dependencyManager;
        private readonly SequencedJobQueue _queue;
        private readonly TrayVerificationService _trayVerifier;
        private readonly CycleOrchestrator _orchestrator;

        public CycleOrchestratorTests()
        {
            _dependencyManager = new JobDependencyManager();
            _queue = new SequencedJobQueue(_dependencyManager);
            _trayVerifier = new TrayVerificationService();
            _orchestrator = new CycleOrchestrator(_dependencyManager, _queue, _trayVerifier, batchSize: 5);
        }

        [Fact]
        public void TestCreateAndEnqueueCycles_CreatesCorrectNumberOfCycles()
        {
            // Arrange
            var printerName = "TestPrinter";
            var startNumber = 1L;
            var totalNumbers = 10L;
            var copiesPerPage = 2;
            var slots = new List<SlotSpec>
            {
                new SlotSpec("slot1", 0.1f, 0.1f, 0.2f, 0.1f, "Arial", 12f, "#000000", TextAlign.Center, 0f, null)
            };
            var trayMapping = new Dictionary<int, PaperSourceKind>
            {
                { 0, PaperSourceKind.Upper },
                { 1, PaperSourceKind.Lower }
            };

            // Act
            var cycles = _orchestrator.CreateAndEnqueueCycles(
                printerName, startNumber, totalNumbers, copiesPerPage, slots, trayMapping);

            // Assert
            Assert.Equal(10, cycles.Count);
            Assert.All(cycles, cycle => Assert.NotNull(cycle));
            Assert.All(cycles, cycle => Assert.Equal(copiesPerPage, cycle.CopiesPerPage));
        }

        [Fact]
        public void TestCreateAndEnqueueCycles_SetsDependenciesCorrectly()
        {
            // Arrange
            var printerName = "TestPrinter";
            var startNumber = 1L;
            var totalNumbers = 5L;
            var copiesPerPage = 1;
            var slots = new List<SlotSpec>
            {
                new SlotSpec("slot1", 0.1f, 0.1f, 0.2f, 0.1f, "Arial", 12f, "#000000", TextAlign.Center, 0f, null)
            };
            var trayMapping = new Dictionary<int, PaperSourceKind>
            {
                { 0, PaperSourceKind.Upper }
            };

            // Act
            var cycles = _orchestrator.CreateAndEnqueueCycles(
                printerName, startNumber, totalNumbers, copiesPerPage, slots, trayMapping);

            // Assert
            // First cycle should have no dependency
            Assert.Null(cycles[0].DependsOnJobId);

            // Subsequent cycles should depend on the previous one
            for (int i = 1; i < cycles.Count; i++)
            {
                Assert.NotNull(cycles[i].DependsOnJobId);
                Assert.Equal(cycles[i - 1].JobId, cycles[i].DependsOnJobId);
            }
        }

        [Fact]
        public void TestCreateAndEnqueueCycles_BatchesPreparation()
        {
            // Arrange
            var printerName = "TestPrinter";
            var startNumber = 1L;
            var totalNumbers = 12L;
            var copiesPerPage = 1;
            var slots = new List<SlotSpec>
            {
                new SlotSpec("slot1", 0.1f, 0.1f, 0.2f, 0.1f, "Arial", 12f, "#000000", TextAlign.Center, 0f, null)
            };
            var trayMapping = new Dictionary<int, PaperSourceKind>
            {
                { 0, PaperSourceKind.Upper }
            };

            // Act
            var cycles = _orchestrator.CreateAndEnqueueCycles(
                printerName, startNumber, totalNumbers, copiesPerPage, slots, trayMapping);

            // Assert
            // Batch size is 5, so:
            // Cycles 0-4 should be in batch-0
            // Cycles 5-9 should be in batch-1
            // Cycles 10-11 should be in batch-2
            Assert.Equal("batch-0", cycles[0].BatchId);
            Assert.Equal("batch-0", cycles[4].BatchId);
            Assert.Equal("batch-1", cycles[5].BatchId);
            Assert.Equal("batch-1", cycles[9].BatchId);
            Assert.Equal("batch-2", cycles[10].BatchId);
            Assert.Equal("batch-2", cycles[11].BatchId);
        }

        [Fact]
        public void TestCreateAndEnqueueCycles_CreatesCorrectPages()
        {
            // Arrange
            var printerName = "TestPrinter";
            var startNumber = 1L;
            var totalNumbers = 3L;
            var copiesPerPage = 3; // Original + Copy1 + Copy2
            var slots = new List<SlotSpec>
            {
                new SlotSpec("slot1", 0.1f, 0.1f, 0.2f, 0.1f, "Arial", 12f, "#000000", TextAlign.Center, 0f, null)
            };
            var trayMapping = new Dictionary<int, PaperSourceKind>
            {
                { 0, PaperSourceKind.Upper },
                { 1, PaperSourceKind.Lower },
                { 2, PaperSourceKind.Middle }
            };

            // Act
            var cycles = _orchestrator.CreateAndEnqueueCycles(
                printerName, startNumber, totalNumbers, copiesPerPage, slots, trayMapping);

            // Assert
            Assert.All(cycles, cycle =>
            {
                Assert.Equal(copiesPerPage, cycle.Pages.Count);
                Assert.Equal(CopyType.Original, cycle.Pages[0].Type);
                Assert.Equal(CopyType.Copy1, cycle.Pages[1].Type);
                Assert.Equal(CopyType.Copy2, cycle.Pages[2].Type);
            });
        }

        [Fact]
        public void TestCreateAndEnqueueCycles_SetsCorrectCycleNumbers()
        {
            // Arrange
            var printerName = "TestPrinter";
            var startNumber = 100L;
            var totalNumbers = 5L;
            var copiesPerPage = 1;
            var slots = new List<SlotSpec>
            {
                new SlotSpec("slot1", 0.1f, 0.1f, 0.2f, 0.1f, "Arial", 12f, "#000000", TextAlign.Center, 0f, null)
            };
            var trayMapping = new Dictionary<int, PaperSourceKind>
            {
                { 0, PaperSourceKind.Upper }
            };

            // Act
            var cycles = _orchestrator.CreateAndEnqueueCycles(
                printerName, startNumber, totalNumbers, copiesPerPage, slots, trayMapping);

            // Assert
            for (int i = 0; i < cycles.Count; i++)
            {
                Assert.Equal(i + 1, cycles[i].CycleNumber);
                Assert.Equal(startNumber + i, cycles[i].StartNumber);
                Assert.Equal(startNumber + i, cycles[i].EndNumber);
            }
        }

        [Fact]
        public void TestCreateAndEnqueueCycles_EnqueuesToQueue()
        {
            // Arrange
            var printerName = "TestPrinter";
            var startNumber = 1L;
            var totalNumbers = 3L;
            var copiesPerPage = 1;
            var slots = new List<SlotSpec>
            {
                new SlotSpec("slot1", 0.1f, 0.1f, 0.2f, 0.1f, "Arial", 12f, "#000000", TextAlign.Center, 0f, null)
            };
            var trayMapping = new Dictionary<int, PaperSourceKind>
            {
                { 0, PaperSourceKind.Upper }
            };

            // Act
            var cycles = _orchestrator.CreateAndEnqueueCycles(
                printerName, startNumber, totalNumbers, copiesPerPage, slots, trayMapping);

            // Assert
            var allJobs = _queue.GetAllJobs();
            Assert.Equal(3, allJobs.Count);
            Assert.All(cycles, cycle => 
                Assert.Contains(allJobs, job => job.JobId == cycle.JobId));
        }

        [Fact]
        public void TestCreateAndEnqueueCycles_FirstCycleIsReady()
        {
            // Arrange
            var printerName = "TestPrinter";
            var startNumber = 1L;
            var totalNumbers = 3L;
            var copiesPerPage = 1;
            var slots = new List<SlotSpec>
            {
                new SlotSpec("slot1", 0.1f, 0.1f, 0.2f, 0.1f, "Arial", 12f, "#000000", TextAlign.Center, 0f, null)
            };
            var trayMapping = new Dictionary<int, PaperSourceKind>
            {
                { 0, PaperSourceKind.Upper }
            };

            // Act
            var cycles = _orchestrator.CreateAndEnqueueCycles(
                printerName, startNumber, totalNumbers, copiesPerPage, slots, trayMapping);

            // Assert
            // First cycle should be ready (no dependencies)
            var firstJob = _queue.DequeueReady();
            Assert.NotNull(firstJob);
            Assert.Equal(cycles[0].JobId, firstJob.JobId);
            Assert.Equal(CycleStatus.Pending, firstJob.Status);
        }
    }
}

