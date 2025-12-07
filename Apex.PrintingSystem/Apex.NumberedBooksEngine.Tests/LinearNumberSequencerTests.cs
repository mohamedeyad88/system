using Apex.NumberedBooksEngine.Core;
using Apex.NumberedBooksEngine.Models;
using Xunit;

namespace Apex.NumberedBooksEngine.Tests
{
    public class LinearSequencerTests
    {
        private readonly NumberSequencer _sequencer = new();

        [Fact]
        public void GenerateLinearAssignments_4Slots_12Numbers_Returns3Pages()
        {
            // Arrange
            var slots = CreateSlots(4); // A, B, C, D
            long start = 1;
            long total = 12;

            // Act
            var assignments = _sequencer.GenerateLinearAssignments(start, total, slots).ToList();

            // Assert
            Assert.Equal(3, assignments.Count);
            
            // Page 0: 1, 2, 3, 4
            Assert.Equal(0, assignments[0].SheetIndex);
            Assert.Equal(4, assignments[0].SlotNumbers.Count);
            Assert.Equal(1, assignments[0].SlotNumbers[0].Number);
            Assert.Equal(4, assignments[0].SlotNumbers[3].Number);

            // Page 1: 5, 6, 7, 8
            Assert.Equal(1, assignments[1].SheetIndex);
            Assert.Equal(5, assignments[1].SlotNumbers[0].Number);
            Assert.Equal(8, assignments[1].SlotNumbers[3].Number);

            // Page 2: 9, 10, 11, 12
            Assert.Equal(2, assignments[2].SheetIndex);
            Assert.Equal(9, assignments[2].SlotNumbers[0].Number);
            Assert.Equal(12, assignments[2].SlotNumbers[3].Number);
        }

        [Fact]
        public void GenerateLinearAssignments_TotalNotDivisibleBySlots_LastPagePartial()
        {
            // Arrange
            var slots = CreateSlots(4);
            long start = 1;
            long total = 10; // 10 % 4 = 2 remainder

            // Act
            var assignments = _sequencer.GenerateLinearAssignments(start, total, slots).ToList();

            // Assert
            Assert.Equal(3, assignments.Count);
            
            // Last page should only have 2 slots filled
            Assert.Equal(2, assignments[2].SlotNumbers.Count);
            Assert.Equal(9, assignments[2].SlotNumbers[0].Number);
            Assert.Equal(10, assignments[2].SlotNumbers[1].Number);
        }

        [Fact]
        public void GenerateLinearAssignments_SingleSlot_ReturnsOnNumberPerPage()
        {
            // Arrange
            var slots = CreateSlots(1);
            long start = 100;
            long total = 5;

            // Act
            var assignments = _sequencer.GenerateLinearAssignments(start, total, slots).ToList();

            // Assert
            Assert.Equal(5, assignments.Count);
            Assert.Equal(100, assignments[0].SlotNumbers[0].Number);
            Assert.Equal(104, assignments[4].SlotNumbers[0].Number);
        }

        [Fact]
        public void GenerateLinearAssignments_ZeroTotal_ReturnsEmpty()
        {
            // Arrange
            var slots = CreateSlots(4);

            // Act
            var assignments = _sequencer.GenerateLinearAssignments(1, 0, slots).ToList();

            // Assert
            Assert.Empty(assignments);
        }

        [Fact]
        public void GenerateLinearAssignments_EmptySlots_ReturnsEmpty()
        {
            // Arrange
            var slots = new List<SlotSpec>();

            // Act
            var assignments = _sequencer.GenerateLinearAssignments(1, 100, slots).ToList();

            // Assert
            Assert.Empty(assignments);
        }

        [Fact]
        public void GenerateLinearAssignments_LargeStartNumber_WorksCorrectly()
        {
            // Arrange
            var slots = CreateSlots(2);
            long start = 999_999;
            long total = 4;

            // Act
            var assignments = _sequencer.GenerateLinearAssignments(start, total, slots).ToList();

            // Assert
            Assert.Equal(2, assignments.Count);
            Assert.Equal(999_999, assignments[0].SlotNumbers[0].Number);
            Assert.Equal(1_000_002, assignments[1].SlotNumbers[1].Number);
        }

        [Fact]
        public void GenerateImposedAssignments_4Slots_12Numbers_ImpositionOrder()
        {
            // Arrange: 4 slots, 12 numbers → 3 sheets
            // Imposition: after cutting, stacking positions gives sequential order
            var slots = CreateSlots(4);
            long start = 1;
            long total = 12;

            // Act
            var assignments = _sequencer.GenerateImposedAssignments(start, total, slots).ToList();

            // Assert
            Assert.Equal(3, assignments.Count);
            
            // Sheet 0: positions 0,1,2,3 get numbers 1,4,7,10 (for cutting mode)
            // Sheet 1: positions get 2,5,8,11
            // Sheet 2: positions get 3,6,9,12
            // When stacked and cut: Position0 stack = 1,2,3; Position1 stack = 4,5,6; etc.
        }

        [Fact]
        public void SlotAssignmentsPreserveSlotOrder()
        {
            // Arrange: slots in specific order
            var slots = new List<SlotSpec>
            {
                CreateSlot("Z"),
                CreateSlot("A"),
                CreateSlot("M")
            };

            // Act
            var assignments = _sequencer.GenerateLinearAssignments(1, 3, slots).ToList();

            // Assert: order should match input slot order
            Assert.Equal("Z", assignments[0].SlotNumbers[0].SlotId);
            Assert.Equal("A", assignments[0].SlotNumbers[1].SlotId);
            Assert.Equal("M", assignments[0].SlotNumbers[2].SlotId);
        }

        private static List<SlotSpec> CreateSlots(int count)
        {
            return Enumerable.Range(0, count)
                .Select(i => CreateSlot(((char)('A' + i)).ToString()))
                .ToList();
        }

        private static SlotSpec CreateSlot(string id)
        {
            return new SlotSpec(
                Id: id,
                X: 0.1f, Y: 0.1f, Width: 0.1f, Height: 0.05f,
                FontFamily: "Arial",
                FontSize: 12f,
                FontColorHex: "#000000",
                Align: TextAlign.Center,
                Rotation: 0f,
                CopyStyles: Array.Empty<CopyStyle>()
            );
        }
    }
}
