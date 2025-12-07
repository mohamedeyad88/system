using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Apex.UI.ViewModels;

namespace Apex.UI.Views
{
    public partial class NumberingWizardView : UserControl
    {
        private NumberSlot? _draggedSlot;
        private Point _dragStartPoint;
        private bool _isDragging;

        public NumberingWizardView()
        {
            InitializeComponent();
            
            // Ensure cursor is always reset when mouse leaves the canvas
            TemplateCanvas.MouseLeave += (s, e) => ResetDragState();
        }

        private NumberingWizardViewModel? ViewModel => DataContext as NumberingWizardViewModel;

        private void ResetDragState()
        {
            _isDragging = false;
            _draggedSlot = null;
            Mouse.OverrideCursor = null;
            TemplateCanvas.ReleaseMouseCapture();
        }

        // Slot selection and drag start
        private void Slot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is NumberSlot slot)
            {
                // Select the slot
                if (ViewModel != null)
                {
                    ViewModel.SelectedSlot = slot;
                }

                // Start drag
                _draggedSlot = slot;
                _dragStartPoint = e.GetPosition(TemplateCanvas);
                _isDragging = true;
                Mouse.OverrideCursor = Cursors.SizeAll;
                TemplateCanvas.CaptureMouse();
                e.Handled = true;
            }
        }

        // Canvas click to add slot at position
        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging) return;

            // Deselect current slot if clicking on empty area
            if (ViewModel != null && e.OriginalSource == TemplateCanvas)
            {
                ViewModel.SelectedSlot = null;
            }
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || _draggedSlot == null || ViewModel == null) return;

            var currentPos = e.GetPosition(TemplateCanvas);
            
            // Calculate new normalized position
            double newX = currentPos.X / ViewModel.CanvasWidth;
            double newY = currentPos.Y / ViewModel.CanvasHeight;
            
            // Smart Snapping (disable if Ctrl is held)
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
            {
                // Snap Threshold (normalized, approx 5 pixels)
                double thresholdX = 5.0 / ViewModel.CanvasWidth;
                double thresholdY = 5.0 / ViewModel.CanvasHeight;

                // Snap to Guidelines (Edges & Center)
                if (Math.Abs(newX - 0.0) < thresholdX) newX = 0.0;
                else if (Math.Abs(newX - 0.5) < thresholdX) newX = 0.5;
                else if (Math.Abs((newX + _draggedSlot.Width) - 1.0) < thresholdX) newX = 1.0 - _draggedSlot.Width;
                else if (Math.Abs((newX + _draggedSlot.Width/2) - 0.5) < thresholdX) newX = 0.5 - _draggedSlot.Width/2;

                if (Math.Abs(newY - 0.0) < thresholdY) newY = 0.0;
                else if (Math.Abs(newY - 0.5) < thresholdY) newY = 0.5;
                else if (Math.Abs((newY + _draggedSlot.Height) - 1.0) < thresholdY) newY = 1.0 - _draggedSlot.Height;
                else if (Math.Abs((newY + _draggedSlot.Height/2) - 0.5) < thresholdY) newY = 0.5 - _draggedSlot.Height/2;
                
                // Snap to Other Slots
                foreach (var other in ViewModel.Slots)
                {
                    if (other == _draggedSlot) continue;

                    // Align Left
                    if (Math.Abs(newX - other.X) < thresholdX) newX = other.X;
                    // Align Center X
                    if (Math.Abs((newX + _draggedSlot.Width/2) - (other.X + other.Width/2)) < thresholdX) 
                        newX = (other.X + other.Width/2) - _draggedSlot.Width/2;
                    // Align Right
                    // ... (Simplifying for readability, core alignments are mostly Left/Center)

                    // Align Top
                    if (Math.Abs(newY - other.Y) < thresholdY) newY = other.Y;
                    // Align Center Y
                     if (Math.Abs((newY + _draggedSlot.Height/2) - (other.Y + other.Height/2)) < thresholdY) 
                        newY = (other.Y + other.Height/2) - _draggedSlot.Height/2;
                }
            }
            
            // Clamp within bounds (0 to 1, accounting for slot size)
            newX = Math.Max(0, Math.Min(1 - _draggedSlot.Width, newX));
            newY = Math.Max(0, Math.Min(1 - _draggedSlot.Height, newY));
            
            _draggedSlot.X = (float)newX;
            _draggedSlot.Y = (float)newY;
        }

        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            ResetDragState();
        }

        // Typography controls
        private void RegularWeight_Click(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel != null)
            {
                ViewModel.IsBold = false;
                if (ViewModel.SelectedSlot != null)
                    ViewModel.SelectedSlot.IsBold = false;
            }
        }

        private void BoldWeight_Click(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel != null)
            {
                ViewModel.IsBold = true;
                if (ViewModel.SelectedSlot != null)
                    ViewModel.SelectedSlot.IsBold = true;
            }
        }

        private void AlignLeft_Click(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel?.SelectedSlot != null)
                ViewModel.SelectedSlot.Alignment = "Left";
        }

        private void AlignCenter_Click(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel?.SelectedSlot != null)
                ViewModel.SelectedSlot.Alignment = "Center";
        }

        private void AlignRight_Click(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel?.SelectedSlot != null)
                ViewModel.SelectedSlot.Alignment = "Right";
        }

        private void Rotation0_Click(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel != null)
            {
                ViewModel.Rotation = 0;
                if (ViewModel.SelectedSlot != null)
                    ViewModel.SelectedSlot.Rotation = 0;
            }
        }

        private void Rotation90_Click(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel != null)
            {
                ViewModel.Rotation = 90;
                if (ViewModel.SelectedSlot != null)
                    ViewModel.SelectedSlot.Rotation = 90;
            }
        }

        private void Rotation180_Click(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel != null)
            {
                ViewModel.Rotation = 180;
                if (ViewModel.SelectedSlot != null)
                    ViewModel.SelectedSlot.Rotation = 180;
            }
        }

        private void Rotation270_Click(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel != null)
            {
                ViewModel.Rotation = 270;
                if (ViewModel.SelectedSlot != null)
                    ViewModel.SelectedSlot.Rotation = 270;
            }
        }
    }
}
