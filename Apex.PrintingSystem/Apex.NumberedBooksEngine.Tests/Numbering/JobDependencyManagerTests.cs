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
    /// Unit tests for JobDependencyManager.
    /// Tests dependency tracking, status updates, and dependency resolution.
    /// </summary>
    public class JobDependencyManagerTests
    {
        private readonly JobDependencyManager _manager;

        public JobDependencyManagerTests()
        {
            _manager = new JobDependencyManager();
        }

        [Fact]
        public void TestAddJob_WithDependency()
        {
            // Arrange
            var parent = CreateCycleJob(1, 1L);
            var child = CreateCycleJob(2, 2L, parent.JobId);

            // Act
            _manager.AddJob(parent);
            _manager.AddJob(child);

            // Assert
            var dependents = _manager.GetDependents(parent.JobId);
            Assert.Single(dependents);
            Assert.Equal(child.JobId, dependents[0]);
        }

        [Fact]
        public void TestAddJob_WithoutDependency()
        {
            // Arrange
            var job = CreateCycleJob(1, 1L);

            // Act
            _manager.AddJob(job);

            // Assert
            var dependents = _manager.GetDependents(job.JobId);
            Assert.Empty(dependents);
        }

        [Fact]
        public void TestUpdateStatus_UpdatesJobStatus()
        {
            // Arrange
            var job = CreateCycleJob(1, 1L);
            _manager.AddJob(job);

            // Act
            var updated = _manager.UpdateStatus(job.JobId, CycleStatus.Printing);

            // Assert
            Assert.NotNull(updated);
            Assert.Equal(CycleStatus.Printing, updated.Status);
            Assert.NotNull(updated.StartedAtUtc);
        }

        [Fact]
        public void TestUpdateStatus_SetsCompletedAtUtc_WhenTerminal()
        {
            // Arrange
            var job = CreateCycleJob(1, 1L);
            _manager.AddJob(job);

            // Act
            var updated = _manager.UpdateStatus(job.JobId, CycleStatus.CompletedPhysical);

            // Assert
            Assert.NotNull(updated);
            Assert.NotNull(updated.CompletedAtUtc);
        }

        [Fact]
        public void TestUpdateStatus_SetsErrorMessage()
        {
            // Arrange
            var job = CreateCycleJob(1, 1L);
            _manager.AddJob(job);
            var errorMessage = "Test error";

            // Act
            var updated = _manager.UpdateStatus(job.JobId, CycleStatus.Failed, errorMessage);

            // Assert
            Assert.NotNull(updated);
            Assert.Equal(errorMessage, updated.ErrorMessage);
        }

        [Fact]
        public void TestAreDependenciesSatisfied_NoDependency_ReturnsTrue()
        {
            // Arrange
            var job = CreateCycleJob(1, 1L);
            _manager.AddJob(job);

            // Act
            var satisfied = _manager.AreDependenciesSatisfied(job);

            // Assert
            Assert.True(satisfied);
        }

        [Fact]
        public void TestAreDependenciesSatisfied_CompletedPhysical_ReturnsTrue()
        {
            // Arrange
            var parent = CreateCycleJob(1, 1L);
            var child = CreateCycleJob(2, 2L, parent.JobId);
            _manager.AddJob(parent);
            _manager.AddJob(child);
            _manager.UpdateStatus(parent.JobId, CycleStatus.CompletedPhysical);

            // Act
            var satisfied = _manager.AreDependenciesSatisfied(child);

            // Assert
            Assert.True(satisfied);
        }

        [Fact]
        public void TestAreDependenciesSatisfied_Skipped_ReturnsTrue()
        {
            // Arrange
            var parent = CreateCycleJob(1, 1L);
            var child = CreateCycleJob(2, 2L, parent.JobId);
            _manager.AddJob(parent);
            _manager.AddJob(child);
            _manager.UpdateStatus(parent.JobId, CycleStatus.Skipped);

            // Act
            var satisfied = _manager.AreDependenciesSatisfied(child);

            // Assert
            Assert.True(satisfied);
        }

        [Fact]
        public void TestAreDependenciesSatisfied_Pending_ReturnsFalse()
        {
            // Arrange
            var parent = CreateCycleJob(1, 1L);
            var child = CreateCycleJob(2, 2L, parent.JobId);
            _manager.AddJob(parent);
            _manager.AddJob(child);
            // Parent remains Pending

            // Act
            var satisfied = _manager.AreDependenciesSatisfied(child);

            // Assert
            Assert.False(satisfied);
        }

        [Fact]
        public void TestAreDependenciesSatisfied_Failed_ReturnsFalse()
        {
            // Arrange
            var parent = CreateCycleJob(1, 1L);
            var child = CreateCycleJob(2, 2L, parent.JobId);
            _manager.AddJob(parent);
            _manager.AddJob(child);
            _manager.UpdateStatus(parent.JobId, CycleStatus.Failed);

            // Act
            var satisfied = _manager.AreDependenciesSatisfied(child);

            // Assert
            Assert.False(satisfied);
        }

        [Fact]
        public void TestGetReadyDependents_ReturnsReadyDependents()
        {
            // Arrange
            var parent = CreateCycleJob(1, 1L);
            var child1 = CreateCycleJob(2, 2L, parent.JobId);
            var child2 = CreateCycleJob(3, 3L, parent.JobId);
            _manager.AddJob(parent);
            _manager.AddJob(child1);
            _manager.AddJob(child2);
            _manager.UpdateStatus(parent.JobId, CycleStatus.CompletedPhysical);

            // Act
            var ready = _manager.GetReadyDependents(parent.JobId).ToList();

            // Assert
            Assert.Equal(2, ready.Count);
            Assert.Contains(ready, j => j.JobId == child1.JobId);
            Assert.Contains(ready, j => j.JobId == child2.JobId);
        }

        [Fact]
        public void TestGetReadyDependents_ExcludesAlreadyCompleted()
        {
            // Arrange
            var parent = CreateCycleJob(1, 1L);
            var child1 = CreateCycleJob(2, 2L, parent.JobId);
            var child2 = CreateCycleJob(3, 3L, parent.JobId);
            _manager.AddJob(parent);
            _manager.AddJob(child1);
            _manager.AddJob(child2);
            _manager.UpdateStatus(child1.JobId, CycleStatus.CompletedPhysical);
            _manager.UpdateStatus(parent.JobId, CycleStatus.CompletedPhysical);

            // Act
            var ready = _manager.GetReadyDependents(parent.JobId).ToList();

            // Assert
            // child1 is already completed, so it should not be in ready list
            Assert.Single(ready);
            Assert.Equal(child2.JobId, ready[0].JobId);
        }

        [Fact]
        public void TestGetReadyDependents_IncludesRetrying()
        {
            // Arrange
            var parent = CreateCycleJob(1, 1L);
            var child = CreateCycleJob(2, 2L, parent.JobId);
            _manager.AddJob(parent);
            _manager.AddJob(child);
            _manager.UpdateStatus(child.JobId, CycleStatus.Failed);
            _manager.UpdateStatus(child.JobId, CycleStatus.Retrying);
            _manager.UpdateStatus(parent.JobId, CycleStatus.CompletedPhysical);

            // Act
            var ready = _manager.GetReadyDependents(parent.JobId).ToList();

            // Assert
            Assert.Single(ready);
            Assert.Equal(CycleStatus.Retrying, ready[0].Status);
        }

        [Fact]
        public void TestGetJob_ReturnsJob()
        {
            // Arrange
            var job = CreateCycleJob(1, 1L);
            _manager.AddJob(job);

            // Act
            var retrieved = _manager.GetJob(job.JobId);

            // Assert
            Assert.NotNull(retrieved);
            Assert.Equal(job.JobId, retrieved.JobId);
        }

        [Fact]
        public void TestGetJob_ReturnsNull_WhenNotFound()
        {
            // Act
            var retrieved = _manager.GetJob("non-existent-id");

            // Assert
            Assert.Null(retrieved);
        }

        [Fact]
        public void TestMultipleDependents_TracksAll()
        {
            // Arrange
            var parent = CreateCycleJob(1, 1L);
            var child1 = CreateCycleJob(2, 2L, parent.JobId);
            var child2 = CreateCycleJob(3, 3L, parent.JobId);
            var child3 = CreateCycleJob(4, 4L, parent.JobId);
            _manager.AddJob(parent);
            _manager.AddJob(child1);
            _manager.AddJob(child2);
            _manager.AddJob(child3);

            // Act
            var dependents = _manager.GetDependents(parent.JobId);

            // Assert
            Assert.Equal(3, dependents.Count);
            Assert.Contains(child1.JobId, dependents);
            Assert.Contains(child2.JobId, dependents);
            Assert.Contains(child3.JobId, dependents);
        }

        private CycleJob CreateCycleJob(int cycleNumber, long number, string? dependsOn = null)
        {
            return new CycleJob
            {
                CycleNumber = cycleNumber,
                StartNumber = number,
                EndNumber = number,
                CopiesPerPage = 1,
                TrayMapping = new Dictionary<int, PaperSourceKind> { { 0, PaperSourceKind.Upper } },
                DependsOnJobId = dependsOn,
                Status = CycleStatus.Pending,
                Pages = new List<CyclePage>
                {
                    new CyclePage
                    {
                        Number = number,
                        Type = CopyType.Original,
                        Tray = PaperSourceKind.Upper,
                        PageIndex = 0,
                        Slots = new List<SlotSpec>
                        {
                            new SlotSpec("slot1", 0.1f, 0.1f, 0.2f, 0.1f, "Arial", 12f, "#000000", TextAlign.Center, 0f, null)
                        }
                    }
                }
            };
        }
    }
}

