using System;

namespace Apex.UI.Models
{
    /// <summary>
    /// Standard paper sizes.
    /// </summary>
    public enum PaperSize
    {
        A4,
        A3,
        Letter,
        Custom
    }

    /// <summary>
    /// Paper orientation.
    /// </summary>
    public enum PaperOrientation
    {
        Portrait,
        Landscape
    }

    /// <summary>
    /// Measurement units for rulers.
    /// </summary>
    public enum MeasurementUnit
    {
        Centimeters,
        Inches,
        Points
    }

    /// <summary>
    /// Helper for paper size calculations.
    /// </summary>
    public static class PaperSizeHelper
    {
        // A4 dimensions in points (at 72 DPI)
        public const double A4WidthPoints = 595.276;
        public const double A4HeightPoints = 841.890;

        // A3 dimensions in points
        public const double A3WidthPoints = 841.890;
        public const double A3HeightPoints = 1190.551;

        // Letter dimensions in points
        public const double LetterWidthPoints = 612;
        public const double LetterHeightPoints = 792;

        /// <summary>
        /// Gets paper dimensions in points.
        /// </summary>
        public static (double Width, double Height) GetDimensions(PaperSize size, PaperOrientation orientation)
        {
            double width, height;

            switch (size)
            {
                case PaperSize.A4:
                    width = A4WidthPoints;
                    height = A4HeightPoints;
                    break;
                case PaperSize.A3:
                    width = A3WidthPoints;
                    height = A3HeightPoints;
                    break;
                case PaperSize.Letter:
                    width = LetterWidthPoints;
                    height = LetterHeightPoints;
                    break;
                default:
                    width = A4WidthPoints;
                    height = A4HeightPoints;
                    break;
            }

            return orientation == PaperOrientation.Landscape
                ? (height, width)
                : (width, height);
        }

        /// <summary>
        /// Converts points to centimeters.
        /// </summary>
        public static double PointsToCentimeters(double points) => points / 28.35;

        /// <summary>
        /// Converts points to inches.
        /// </summary>
        public static double PointsToInches(double points) => points / 72.0;

        /// <summary>
        /// Converts centimeters to points.
        /// </summary>
        public static double CentimetersToPoints(double cm) => cm * 28.35;

        /// <summary>
        /// Converts inches to points.
        /// </summary>
        public static double InchesToPoints(double inches) => inches * 72.0;

        /// <summary>
        /// Formats a value in points according to the selected unit.
        /// </summary>
        public static string FormatValue(double points, MeasurementUnit unit)
        {
            return unit switch
            {
                MeasurementUnit.Centimeters => $"{PointsToCentimeters(points):F1}",
                MeasurementUnit.Inches => $"{PointsToInches(points):F2}",
                MeasurementUnit.Points => $"{(int)points}",
                _ => $"{(int)points}"
            };
        }
    }
}
