using Apex.NumberedBooksEngine.Models;
using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.Linq;

namespace Apex.NumberedBooksEngine.Core
{
    /// <summary>
    /// Validation error with severity level.
    /// </summary>
    public class ValidationError
    {
        public string Message { get; set; } = "";
        public ValidationSeverity Severity { get; set; } = ValidationSeverity.Error;
        public string? SlotId { get; set; }
        public string Code { get; set; } = "";
    }

    public enum ValidationSeverity
    {
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// Validates job options before printing.
    /// </summary>
    public static class JobValidator
    {
        private const float SafeMarginNormalized = 0.02f; // ~5mm on A4

        /// <summary>
        /// Validates the given job options and returns a list of errors/warnings.
        /// </summary>
        public static List<ValidationError> Validate(BookJobOptions options, string? printerName = null)
        {
            var errors = new List<ValidationError>();

            // Check template
            ValidateTemplate(options, errors);

            // Check slots
            ValidateSlots(options, errors);

            // Check number count
            ValidateNumbers(options, errors);

            // Check printer
            ValidatePrinter(printerName, errors);

            // Check mode vs layout consistency
            ValidateModeLayoutConsistency(options, errors);

            // Check copy styles
            ValidateCopyStyles(options, errors);

            return errors;
        }

        private static void ValidateTemplate(BookJobOptions options, List<ValidationError> errors)
        {
            if (options.TemplateStream == null && string.IsNullOrEmpty(options.TemplatePath))
            {
                errors.Add(new ValidationError
                {
                    Code = "NO_TEMPLATE",
                    Message = "No template loaded. Please load a template first.",
                    Severity = ValidationSeverity.Error
                });
            }
        }

        private static void ValidateSlots(BookJobOptions options, List<ValidationError> errors)
        {
            if (options.Slots == null || options.Slots.Count == 0)
            {
                errors.Add(new ValidationError
                {
                    Code = "NO_SLOTS",
                    Message = "No numbering slots defined. Please add at least one slot.",
                    Severity = ValidationSeverity.Error
                });
                return;
            }

            for (int i = 0; i < options.Slots.Count; i++)
            {
                var slot = options.Slots[i];

                // Check slot outside template
                if (slot.X < 0 || slot.X > 1 || slot.Y < 0 || slot.Y > 1 ||
                    slot.X + slot.Width > 1 || slot.Y + slot.Height > 1)
                {
                    errors.Add(new ValidationError
                    {
                        Code = "SLOT_OUTSIDE",
                        Message = $"Slot #{i + 1} extends beyond template area.",
                        Severity = ValidationSeverity.Warning,
                        SlotId = slot.Id
                    });
                }

                // Check slot in safe margin
                if (slot.X < SafeMarginNormalized || slot.Y < SafeMarginNormalized ||
                    slot.X + slot.Width > 1 - SafeMarginNormalized ||
                    slot.Y + slot.Height > 1 - SafeMarginNormalized)
                {
                    errors.Add(new ValidationError
                    {
                        Code = "SLOT_MARGIN",
                        Message = $"⚠ Slot #{i + 1} extends beyond printable margins.",
                        Severity = ValidationSeverity.Warning,
                        SlotId = slot.Id
                    });
                }

                // Check for overlapping slots
                for (int j = i + 1; j < options.Slots.Count; j++)
                {
                    var other = options.Slots[j];
                    if (SlotsOverlap(slot, other))
                    {
                        errors.Add(new ValidationError
                        {
                            Code = "SLOT_OVERLAP",
                            Message = $"Slot #{i + 1} overlaps with Slot #{j + 1}.",
                            Severity = ValidationSeverity.Warning,
                            SlotId = slot.Id
                        });
                    }
                }
            }
        }

        private static bool SlotsOverlap(SlotSpec a, SlotSpec b)
        {
            return !(a.X + a.Width <= b.X || b.X + b.Width <= a.X ||
                     a.Y + a.Height <= b.Y || b.Y + b.Height <= a.Y);
        }

        private static void ValidateNumbers(BookJobOptions options, List<ValidationError> errors)
        {
            if (options.TotalNumbers <= 0)
            {
                errors.Add(new ValidationError
                {
                    Code = "INVALID_TOTAL",
                    Message = "Total numbers must be greater than 0.",
                    Severity = ValidationSeverity.Error
                });
            }

            if (options.Slots != null && options.TotalNumbers < options.Slots.Count)
            {
                errors.Add(new ValidationError
                {
                    Code = "TOO_FEW_NUMBERS",
                    Message = "Total numbers is less than slot count. Some slots will be empty.",
                    Severity = ValidationSeverity.Warning
                });
            }
        }

        private static void ValidatePrinter(string? printerName, List<ValidationError> errors)
        {
            if (!string.IsNullOrEmpty(printerName))
            {
                var installedPrinters = PrinterSettings.InstalledPrinters.Cast<string>().ToList();
                if (!installedPrinters.Contains(printerName))
                {
                    errors.Add(new ValidationError
                    {
                        Code = "PRINTER_NOT_FOUND",
                        Message = $"Printer '{printerName}' is not available.",
                        Severity = ValidationSeverity.Error
                    });
                }
            }
        }

        /// <summary>
        /// Validates that the selected mode matches the slot layout.
        /// </summary>
        private static void ValidateModeLayoutConsistency(BookJobOptions options, List<ValidationError> errors)
        {
            if (options.Slots == null || options.Slots.Count == 0 || options.Mode == NumberingMode.Auto)
                return;

            var detectedMode = LayoutAutoDetector.DetectMode(options.Slots);
            var selectedMode = options.Mode;

            // Normalize aliases
            if (selectedMode == NumberingMode.Shershara) selectedMode = NumberingMode.Linear;
            if (selectedMode == NumberingMode.Cutting) selectedMode = NumberingMode.Imposed;
            if (detectedMode == NumberingMode.Shershara) detectedMode = NumberingMode.Linear;
            if (detectedMode == NumberingMode.Cutting) detectedMode = NumberingMode.Imposed;

            if (selectedMode != detectedMode && selectedMode != NumberingMode.Custom)
            {
                string detectedName = detectedMode == NumberingMode.Linear ? "Shershara" : "Cutting";
                string selectedName = selectedMode == NumberingMode.Linear ? "Shershara" : "Cutting";

                errors.Add(new ValidationError
                {
                    Code = "MODE_MISMATCH",
                    Message = $"⚠ Slots suggest \"{detectedName}\" mode but user selected \"{selectedName}\".",
                    Severity = ValidationSeverity.Warning
                });
            }
        }

        /// <summary>
        /// Validates copy style definitions.
        /// </summary>
        private static void ValidateCopyStyles(BookJobOptions options, List<ValidationError> errors)
        {
            if (options.Slots == null) return;

            foreach (var slot in options.Slots)
            {
                if (slot.CopyStyles == null) continue;

                for (int i = 0; i < slot.CopyStyles.Length; i++)
                {
                    var copyStyle = slot.CopyStyles[i];

                    if (copyStyle.Opacity <= 0)
                    {
                        errors.Add(new ValidationError
                        {
                            Code = "COPY_INVISIBLE",
                            Message = $"⚠ Copy #{i + 1} opacity = {copyStyle.Opacity * 100:F0}% (invisible).",
                            Severity = ValidationSeverity.Warning,
                            SlotId = slot.Id
                        });
                    }

                    if (string.IsNullOrEmpty(copyStyle.ColorHex))
                    {
                        errors.Add(new ValidationError
                        {
                            Code = "COPY_NO_COLOR",
                            Message = $"Copy #{i + 1} has no color defined.",
                            Severity = ValidationSeverity.Warning,
                            SlotId = slot.Id
                        });
                    }
                }
            }
        }

        /// <summary>
        /// Returns true if there are no errors (warnings are allowed).
        /// </summary>
        public static bool IsValid(List<ValidationError> errors)
        {
            return !errors.Any(e => e.Severity == ValidationSeverity.Error);
        }

        /// <summary>
        /// Formats errors for display.
        /// </summary>
        public static string FormatErrors(List<ValidationError> errors)
        {
            return string.Join("\n", errors.Select(e => 
                e.Severity == ValidationSeverity.Error ? $"❌ {e.Message}" :
                e.Severity == ValidationSeverity.Warning ? $"⚠ {e.Message}" :
                $"ℹ {e.Message}"));
        }
    }
}

