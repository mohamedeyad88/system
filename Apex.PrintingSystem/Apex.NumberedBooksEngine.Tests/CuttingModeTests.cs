using Apex.NumberedBooksEngine.Core;
using Apex.NumberedBooksEngine.Models;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Apex.NumberedBooksEngine.Tests
{
    public class CuttingModeTests
    {
        [Fact]
        public void ImposedSequence_StandardCutting_DistributesCorrectly()
        {
            // Arrange
            var sequencer = new NumberSequencer();
            var slots = new List<SlotSpec>
            {
                new SlotSpec("S1", 0,0,0,0, "", 10, "", TextAlign.Left, 0, null),
                new SlotSpec("S2", 0,0,0,0, "", 10, "", TextAlign.Left, 0, null)
            };
            
            long totalNumbers = 100;
            // 2 slots -> 50 sheets
            // Slot 1: 1..50
            // Slot 2: 51..100
            
            // Act
            var assignments = sequencer.GenerateImposedAssignments(1, totalNumbers, slots).ToList();

            // Assert
            Assert.Equal(50, assignments.Count);
            
            // Check Sheet 0
            var s0 = assignments[0];
            Assert.Equal(1, s0.SlotNumbers.First(s => s.SlotId == "S1").Number);
            Assert.Equal(51, s0.SlotNumbers.First(s => s.SlotId == "S2").Number);
            
            // Check Last Sheet (Sheet 49)
            var sLast = assignments[49];
            Assert.Equal(50, sLast.SlotNumbers.First(s => s.SlotId == "S1").Number);
            Assert.Equal(100, sLast.SlotNumbers.First(s => s.SlotId == "S2").Number);
        }

        [Fact]
        public void ImposedSequence_WithStepping_ShouldIncrementCorrectly()
        {
            var sequencer = new NumberSequencer();
            var slots = new List<SlotSpec>
            {
                new SlotSpec("S1", 0,0,0,0, "", 10, "", TextAlign.Left, 0, null),
                new SlotSpec("S2", 0,0,0,0, "", 10, "", TextAlign.Left, 0, null)
            };
            
            // Start=1, Count=4, Step=10
            // Indicies: 0, 1, 2, 3
            // Values: 1, 11, 21, 31
            
            // Sheets needed for 4 items over 2 slots: 2 sheets.
            // S1 on Sheet 0: Index 0 -> Value 1
            // S2 on Sheet 0: Index 0 + (1*2) = 2 -> Value 1 + 20 = 21
            // S1 on Sheet 1: Index 1 -> Value 1 + 10 = 11
            // S2 on Sheet 1: Index 1 + 2 = 3 -> Value 1 + 30 = 31
            
            var assignments = sequencer.GenerateImposedAssignments(1, 4, slots, 1, 10).ToList();

            Assert.Equal(2, assignments.Count);
            
            // Sheet 0
            var s0 = assignments[0];
            Assert.Equal(1, s0.SlotNumbers.First(s => s.SlotId == "S1").Number);
            Assert.Equal(21, s0.SlotNumbers.First(s => s.SlotId == "S2").Number);

            // Sheet 1
            var s1 = assignments[1];
            Assert.Equal(11, s1.SlotNumbers.First(s => s.SlotId == "S1").Number);
            Assert.Equal(31, s1.SlotNumbers.First(s => s.SlotId == "S2").Number);
        }

        [Fact]
        public void LinearSequence_WithStepping_ShouldIncrementSequentially()
        {
            var sequencer = new NumberSequencer();
            var slots = new List<SlotSpec>
            {
                new SlotSpec("S1", 0,0,0,0, "", 10, "", TextAlign.Left, 0, null),
                new SlotSpec("S2", 0,0,0,0, "", 10, "", TextAlign.Left, 0, null)
            };
            
            // Start=1, Count=4, Step=10
            // Values: 1, 11, 21, 31
            
            // Linear fill:
            // Sheet 0: S1->1, S2->11
            // Sheet 1: S1->21, S2->31
            
            var assignments = sequencer.GenerateLinearAssignments(1, 4, slots, 10).ToList();

            Assert.Equal(2, assignments.Count);
            
            var s0 = assignments[0];
            Assert.Equal(1, s0.SlotNumbers.First(s => s.SlotId == "S1").Number);
            Assert.Equal(11, s0.SlotNumbers.First(s => s.SlotId == "S2").Number);
            
            var s1 = assignments[1];
            Assert.Equal(21, s1.SlotNumbers.First(s => s.SlotId == "S1").Number);
            Assert.Equal(31, s1.SlotNumbers.First(s => s.SlotId == "S2").Number);
        }
    }
}
