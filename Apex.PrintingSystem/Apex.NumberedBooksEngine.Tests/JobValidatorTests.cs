using Xunit;
using Apex.NumberedBooksEngine.Core;
using Apex.NumberedBooksEngine.Models;
using System.Collections.Generic;
using System.Linq;

namespace Apex.NumberedBooksEngine.Tests
{
    /// <summary>
    /// Integration tests for the JobValidator pre-flight check system.
    /// </summary>
    public class JobValidatorTests
    {
        [Fact]
        public void Validate_NoTemplate_ReturnsError()
        {
            // Arrange
            var options = CreateMinimalOptions(templatePath: null);

            // Act
            var errors = JobValidator.Validate(options);

            // Assert
            Assert.Contains(errors, e => e.Code == "NO_TEMPLATE" && e.Severity == ValidationSeverity.Error);
            Assert.False(JobValidator.IsValid(errors));
        }

        [Fact]
        public void Validate_NoSlots_ReturnsError()
        {
            // Arrange
            var options = CreateMinimalOptions(slotCount: 0);

            // Act
            var errors = JobValidator.Validate(options);

            // Assert
            Assert.Contains(errors, e => e.Code == "NO_SLOTS" && e.Severity == ValidationSeverity.Error);
            Assert.False(JobValidator.IsValid(errors));
        }

        [Fact]
        public void Validate_InvalidTotalNumbers_ReturnsError()
        {
            // Arrange
            var options = CreateMinimalOptions(totalNumbers: 0);

            // Act
            var errors = JobValidator.Validate(options);

            // Assert
            Assert.Contains(errors, e => e.Code == "INVALID_TOTAL" && e.Severity == ValidationSeverity.Error);
        }

        [Fact]
        public void Validate_TooFewNumbers_ReturnsWarning()
        {
            // Arrange
            var options = CreateMinimalOptions(slotCount: 5, totalNumbers: 3);

            // Act
            var errors = JobValidator.Validate(options);

            // Assert
            Assert.Contains(errors, e => e.Code == "TOO_FEW_NUMBERS" && e.Severity == ValidationSeverity.Warning);
            // Warnings still allow job to proceed
            Assert.True(JobValidator.IsValid(errors));
        }

        [Fact]
        public void Validate_SlotOutsideBounds_ReturnsWarning()
        {
            // Arrange
            var slots = new List<SlotSpec>
            {
                new SlotSpec("slot1", -0.1f, 0.5f, 0.1f, 0.05f, "Arial", 24, "#000000", TextAlign.Center, 0, null)
            };
            var options = CreateMinimalOptions(slots: slots);

            // Act
            var errors = JobValidator.Validate(options);

            // Assert
            Assert.Contains(errors, e => e.Code == "SLOT_OUTSIDE" && e.Severity == ValidationSeverity.Warning);
        }

        [Fact]
        public void Validate_OverlappingSlots_ReturnsWarning()
        {
            // Arrange
            var slots = new List<SlotSpec>
            {
                new SlotSpec("slot1", 0.1f, 0.1f, 0.3f, 0.2f, "Arial", 24, "#000000", TextAlign.Center, 0, null),
                new SlotSpec("slot2", 0.2f, 0.15f, 0.3f, 0.2f, "Arial", 24, "#000000", TextAlign.Center, 0, null) // Overlaps with slot1
            };
            var options = CreateMinimalOptions(slots: slots);

            // Act
            var errors = JobValidator.Validate(options);

            // Assert
            Assert.Contains(errors, e => e.Code == "SLOT_OVERLAP" && e.Severity == ValidationSeverity.Warning);
        }

        [Fact]
        public void Validate_ValidOptions_ReturnsNoErrors()
        {
            // Arrange
            var slots = new List<SlotSpec>
            {
                new SlotSpec("slot1", 0.1f, 0.1f, 0.1f, 0.05f, "Arial", 24, "#000000", TextAlign.Center, 0, null),
                new SlotSpec("slot2", 0.6f, 0.6f, 0.1f, 0.05f, "Arial", 24, "#000000", TextAlign.Center, 0, null)
            };
            var options = CreateMinimalOptions(slots: slots, totalNumbers: 1000);

            // Act
            var errors = JobValidator.Validate(options);

            // Assert - may have warnings but no errors
            Assert.True(JobValidator.IsValid(errors));
        }

        [Fact]
        public void FormatErrors_FormatsCorrectly()
        {
            // Arrange
            var errors = new List<ValidationError>
            {
                new ValidationError { Code = "E1", Message = "Error message", Severity = ValidationSeverity.Error },
                new ValidationError { Code = "W1", Message = "Warning message", Severity = ValidationSeverity.Warning },
                new ValidationError { Code = "I1", Message = "Info message", Severity = ValidationSeverity.Info }
            };

            // Act
            var formatted = JobValidator.FormatErrors(errors);

            // Assert
            Assert.Contains("❌ Error message", formatted);
            Assert.Contains("⚠ Warning message", formatted);
            Assert.Contains("ℹ Info message", formatted);
        }

        private BookJobOptions CreateMinimalOptions(
            string? templatePath = "test.pdf",
            int slotCount = 2,
            long totalNumbers = 100,
            IReadOnlyList<SlotSpec>? slots = null)
        {
            slots ??= Enumerable.Range(0, slotCount)
                .Select(i => new SlotSpec(
                    $"slot{i + 1}",
                    0.1f + (i * 0.4f), 0.1f,
                    0.1f, 0.05f,
                    "Arial", 24, "#000000",
                    TextAlign.Center, 0, null
                ))
                .ToList();

            return new BookJobOptions(
                TemplateStream: templatePath != null ? new System.IO.MemoryStream() : null,
                TemplatePath: templatePath,
                TemplateFormat: TemplateFormat.Pdf,
                Layout: LayoutSpec.A4,
                Slots: slots,
                StartNumber: 1,
                TotalNumbers: totalNumbers,
                PagesPerBook: 50,
                CopiesPerPage: 1,
                Mode: NumberingMode.Linear,
                LowResourceMode: false,
                DegreeOfParallelism: 4,
                CheckpointEvery: 100,
                OutputMode: "SinglePdf",
                OutputPath: "output.pdf"
            );
        }
    }
}
