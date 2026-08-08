using Apex.NumberedBooksEngine.Core;
using Apex.NumberedBooksEngine.Models;
using System.Collections.Generic;
using Xunit;

namespace Apex.NumberedBooksEngine.Tests
{
    public class MultiCopyStreamingTests
    {
        private readonly PrintCommandBuilder _commandBuilder = new();

        private SlotSpec CreateTestSlot(string id, CopyStyle[]? copyStyles = null)
        {
            return new SlotSpec(
                Id: id,
                X: 0.1f, Y: 0.1f, Width: 0.2f, Height: 0.05f,
                FontFamily: "Arial",
                FontSize: 12f,
                FontColorHex: "#000000",
                Align: TextAlign.Left,
                Rotation: 0f,
                CopyStyles: copyStyles ?? new CopyStyle[0]
            );
        }

        [Fact]
        public void BuildGdiCommandWithCopyStyle_AppliesCorrectStyle()
        {
            // Arrange
            var copyStyles = new[]
            {
                new CopyStyle("Original", "#000000", 1.0f),
                new CopyStyle("Copy", "#FF0000", 0.5f)
            };
            var slots = new List<SlotSpec> { CreateTestSlot("slot1", copyStyles) };
            var pageNumbers = new long[] { 1 };

            // Act - Original
            var originalCommand = _commandBuilder.BuildGdiCommandWithCopyStyle(
                "template1", pageNumbers, slots, 300, CopyType.Original);

            // Act - Copy1
            var copy1Command = _commandBuilder.BuildGdiCommandWithCopyStyle(
                "template1", pageNumbers, slots, 300, CopyType.Copy1);

            // Assert
            Assert.Single(originalCommand.Slots);
            Assert.Single(copy1Command.Slots);

            Assert.Equal("#000000", originalCommand.Slots[0].ColorHex);
            Assert.Equal("#FF0000", copy1Command.Slots[0].ColorHex);

            Assert.Equal("Original", originalCommand.Slots[0].Label);
            Assert.Equal("Copy", copy1Command.Slots[0].Label);
        }

        [Fact]
        public void BuildGdiCommandWithCopyStyle_FallsBackToFirstStyle()
        {
            // Arrange - Only one style defined
            var copyStyles = new[]
            {
                new CopyStyle("Original", "#000000", 1.0f)
            };
            var slots = new List<SlotSpec> { CreateTestSlot("slot1", copyStyles) };
            var pageNumbers = new long[] { 1 };

            // Act - Request Copy2 which doesn't exist
            var command = _commandBuilder.BuildGdiCommandWithCopyStyle(
                "template1", pageNumbers, slots, 300, CopyType.Copy2);

            // Assert - Should fallback to first style
            Assert.Single(command.Slots);
            Assert.Equal("#000000", command.Slots[0].ColorHex);
            Assert.Equal("Original", command.Slots[0].Label);
        }

        [Fact]
        public void BuildGdiCommandWithCopyStyle_FormatsLabelCorrectly()
        {
            // Arrange
            var copyStyles = new[]
            {
                new CopyStyle("أصل", "#000000", 1.0f),  // Arabic "Original"
            };
            var slots = new List<SlotSpec> { CreateTestSlot("slot1", copyStyles) };
            var pageNumbers = new long[] { 42 };

            // Act
            var command = _commandBuilder.BuildGdiCommandWithCopyStyle(
                "template1", pageNumbers, slots, 300, CopyType.Original);

            // Assert - Number padded to 6 digits with label
            Assert.Contains("000042", command.Slots[0].Text);
            Assert.Contains("أصل", command.Slots[0].Text);
        }

        [Fact]
        public void BuildGdiCommandWithCopyStyle_HandlesNullCopyStyles()
        {
            // Arrange - No copy styles defined
            var slots = new List<SlotSpec> { CreateTestSlot("slot1", null) };
            var pageNumbers = new long[] { 123 };

            // Act
            var command = _commandBuilder.BuildGdiCommandWithCopyStyle(
                "template1", pageNumbers, slots, 300, CopyType.Original);

            // Assert - Should use default font color, no label
            Assert.Single(command.Slots);
            Assert.Equal("#000000", command.Slots[0].ColorHex);
            Assert.Null(command.Slots[0].Label);
            Assert.Equal("000123", command.Slots[0].Text);
        }

        [Fact]
        public void BuildGdiCommandWithCopyStyle_SkipsNegativeNumbers()
        {
            // Arrange
            var slots = new List<SlotSpec>
            {
                CreateTestSlot("slot1"),
                CreateTestSlot("slot2")
            };
            var pageNumbers = new long[] { 1, -1 }; // Second slot is empty

            // Act
            var command = _commandBuilder.BuildGdiCommandWithCopyStyle(
                "template1", pageNumbers, slots, 300, CopyType.Original);

            // Assert - Only one slot overlay (empty slot skipped)
            Assert.Single(command.Slots);
            Assert.Equal("slot1", command.Slots[0].SlotId);
        }
    }
}
