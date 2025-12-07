using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Apex.NumberedBooksEngine.UI.Helpers
{
    /// <summary>
    /// Provides snapping functionality for slot positioning.
    /// </summary>
    public class SnapHelper
    {
        public double GridSize { get; set; } = 10;
        public double SnapThreshold { get; set; } = 5;
        public bool SnapToGrid { get; set; } = true;
        public bool SnapToSlots { get; set; } = true;
        public bool SnapToCenter { get; set; } = true;

        private readonly List<Rect> _otherSlotBounds = new();
        private Size _canvasSize;

        /// <summary>
        /// Updates the canvas size for center snapping.
        /// </summary>
        public void SetCanvasSize(Size size)
        {
            _canvasSize = size;
        }

        /// <summary>
        /// Updates the bounds of other slots for edge snapping.
        /// </summary>
        public void SetOtherSlots(IEnumerable<Rect> bounds)
        {
            _otherSlotBounds.Clear();
            _otherSlotBounds.AddRange(bounds);
        }

        /// <summary>
        /// Snaps a point considering all enabled snap modes.
        /// </summary>
        public Point Snap(Point point, Rect? currentSlotBounds = null)
        {
            var result = point;
            var guides = new List<SnapGuide>();

            // 1. Snap to grid
            if (SnapToGrid)
            {
                var gridSnapped = SnapToGridLine(point);
                if (Math.Abs(gridSnapped.X - point.X) < SnapThreshold)
                {
                    result.X = gridSnapped.X;
                    guides.Add(new SnapGuide(SnapGuideType.Grid, gridSnapped.X, true));
                }
                if (Math.Abs(gridSnapped.Y - point.Y) < SnapThreshold)
                {
                    result.Y = gridSnapped.Y;
                    guides.Add(new SnapGuide(SnapGuideType.Grid, gridSnapped.Y, false));
                }
            }

            // 2. Snap to center
            if (SnapToCenter && _canvasSize.Width > 0)
            {
                double centerX = _canvasSize.Width / 2;
                double centerY = _canvasSize.Height / 2;

                if (Math.Abs(point.X - centerX) < SnapThreshold)
                {
                    result.X = centerX;
                    guides.Add(new SnapGuide(SnapGuideType.Center, centerX, true));
                }
                if (Math.Abs(point.Y - centerY) < SnapThreshold)
                {
                    result.Y = centerY;
                    guides.Add(new SnapGuide(SnapGuideType.Center, centerY, false));
                }
            }

            // 3. Snap to other slot edges
            if (SnapToSlots && currentSlotBounds.HasValue)
            {
                result = SnapToSlotEdges(result, currentSlotBounds.Value, guides);
            }

            LastSnapGuides = guides;
            return result;
        }

        /// <summary>
        /// Last calculated snap guides (for visual feedback).
        /// </summary>
        public List<SnapGuide> LastSnapGuides { get; private set; } = new();

        private Point SnapToGridLine(Point point)
        {
            return new Point(
                Math.Round(point.X / GridSize) * GridSize,
                Math.Round(point.Y / GridSize) * GridSize
            );
        }

        private Point SnapToSlotEdges(Point point, Rect currentBounds, List<SnapGuide> guides)
        {
            var result = point;

            foreach (var other in _otherSlotBounds)
            {
                // Skip if same slot
                if (other == currentBounds) continue;

                // Snap left edge to other slot edges
                if (Math.Abs(currentBounds.Left - other.Left) < SnapThreshold)
                {
                    result.X = other.Left;
                    guides.Add(new SnapGuide(SnapGuideType.SlotEdge, other.Left, true));
                }
                else if (Math.Abs(currentBounds.Left - other.Right) < SnapThreshold)
                {
                    result.X = other.Right;
                    guides.Add(new SnapGuide(SnapGuideType.SlotEdge, other.Right, true));
                }

                // Snap right edge to other slot edges
                if (Math.Abs(currentBounds.Right - other.Left) < SnapThreshold)
                {
                    result.X = other.Left - currentBounds.Width;
                    guides.Add(new SnapGuide(SnapGuideType.SlotEdge, other.Left, true));
                }
                else if (Math.Abs(currentBounds.Right - other.Right) < SnapThreshold)
                {
                    result.X = other.Right - currentBounds.Width;
                    guides.Add(new SnapGuide(SnapGuideType.SlotEdge, other.Right, true));
                }

                // Snap top edge
                if (Math.Abs(currentBounds.Top - other.Top) < SnapThreshold)
                {
                    result.Y = other.Top;
                    guides.Add(new SnapGuide(SnapGuideType.SlotEdge, other.Top, false));
                }
                else if (Math.Abs(currentBounds.Top - other.Bottom) < SnapThreshold)
                {
                    result.Y = other.Bottom;
                    guides.Add(new SnapGuide(SnapGuideType.SlotEdge, other.Bottom, false));
                }

                // Snap bottom edge
                if (Math.Abs(currentBounds.Bottom - other.Top) < SnapThreshold)
                {
                    result.Y = other.Top - currentBounds.Height;
                    guides.Add(new SnapGuide(SnapGuideType.SlotEdge, other.Top, false));
                }
                else if (Math.Abs(currentBounds.Bottom - other.Bottom) < SnapThreshold)
                {
                    result.Y = other.Bottom - currentBounds.Height;
                    guides.Add(new SnapGuide(SnapGuideType.SlotEdge, other.Bottom, false));
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Snap guide information for visual feedback.
    /// </summary>
    public record SnapGuide(SnapGuideType Type, double Position, bool IsVertical);

    public enum SnapGuideType
    {
        Grid,
        Center,
        SlotEdge,
        SafeMargin
    }
}
