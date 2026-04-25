using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Apex.UI.ViewModels;
using Apex.UI.Models;
using Apex.UI.Views.Dialogs;

namespace Apex.UI.Views
{
    public partial class NumberingWizardView : UserControl
    {
        private NumberSlot? _draggedSlot;
        private Point _dragStartPoint;
        private bool _isDragging;
        private GuideLine? _draggedGuide;
        private bool _isDraggingGuide;

        public NumberingWizardView()
        {
            InitializeComponent();
            TemplateCanvas.MouseLeave += (s, e) => ResetDragState();

            // Mouse wheel zoom on canvas (Ctrl+Wheel)
            TemplateCanvas.MouseWheel += (s, e) =>
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                {
                    ViewModel?.ApplyWheelZoom(e.Delta);
                    e.Handled = true;
                }
            };

            // Also allow wheel zoom on the outer scroll viewer area
            this.PreviewMouseWheel += (s, e) =>
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                {
                    ViewModel?.ApplyWheelZoom(e.Delta);
                    e.Handled = true;
                }
            };
        }

        private NumberingWizardViewModel? ViewModel => DataContext as NumberingWizardViewModel;

        private void StartLayoutButton_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = false;
        }

        private void StartLayoutButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;

            if (ViewModel.StartLayoutCommand != null)
            {
                if (ViewModel.StartLayoutCommand.CanExecute(null))
                {
                    ViewModel.StartLayoutCommand.Execute(null);
                }
                else if (!string.IsNullOrEmpty(ViewModel.TemplatePath))
                {
                    ViewModel.WorkflowMode = WorkflowMode.Design;
                }
            }
            else if (!string.IsNullOrEmpty(ViewModel.TemplatePath))
            {
                ViewModel.WorkflowMode = WorkflowMode.Design;
            }
            else
            {
                MessageBox.Show("يرجى إدراج التصميم أولاً", "تحذير", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void StartPrintButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null && ViewModel.StartPrintCommand.CanExecute(null))
                ViewModel.StartPrintCommand.Execute(null);
        }

        private void ResetDragState()
        {
            _isDragging = false;
            _draggedSlot = null;
            _isDraggingGuide = false;
            _draggedGuide = null;
            Mouse.OverrideCursor = null;
            TemplateCanvas.ReleaseMouseCapture();
        }

        private void Slot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is NumberSlot slot)
            {
                if (ViewModel != null)
                    ViewModel.SelectedSlot = slot;

                _draggedSlot = slot;
                _dragStartPoint = e.GetPosition(TemplateCanvas);
                _isDragging = true;
                Mouse.OverrideCursor = Cursors.SizeAll;
                TemplateCanvas.CaptureMouse();
                e.Handled = true;
            }
        }

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging || _isDraggingGuide) return;

            if (ViewModel != null && e.OriginalSource == TemplateCanvas)
                ViewModel.SelectedSlot = null;
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || _draggedSlot == null || ViewModel == null) return;

            var currentPos = e.GetPosition(TemplateCanvas);

            double newX = currentPos.X / ViewModel.CanvasWidth;
            double newY = currentPos.Y / ViewModel.CanvasHeight;

            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
            {
                double thresholdX = 5.0 / ViewModel.CanvasWidth;
                double thresholdY = 5.0 / ViewModel.CanvasHeight;

                if (Math.Abs(newX - 0.0) < thresholdX) newX = 0.0;
                else if (Math.Abs(newX - 0.5) < thresholdX) newX = 0.5;
                else if (Math.Abs((newX + _draggedSlot.Width) - 1.0) < thresholdX) newX = 1.0 - _draggedSlot.Width;
                else if (Math.Abs((newX + _draggedSlot.Width / 2) - 0.5) < thresholdX) newX = 0.5 - _draggedSlot.Width / 2;

                if (Math.Abs(newY - 0.0) < thresholdY) newY = 0.0;
                else if (Math.Abs(newY - 0.5) < thresholdY) newY = 0.5;
                else if (Math.Abs((newY + _draggedSlot.Height) - 1.0) < thresholdY) newY = 1.0 - _draggedSlot.Height;
                else if (Math.Abs((newY + _draggedSlot.Height / 2) - 0.5) < thresholdY) newY = 0.5 - _draggedSlot.Height / 2;

                foreach (var guide in ViewModel.Guides)
                {
                    if (guide.Orientation == System.Windows.Controls.Orientation.Vertical)
                    {
                        double guideX = guide.Position / ViewModel.CanvasWidth;
                        if (Math.Abs(newX - guideX) < thresholdX) newX = guideX;
                        if (Math.Abs((newX + _draggedSlot.Width / 2) - guideX) < thresholdX)
                            newX = guideX - _draggedSlot.Width / 2;
                    }
                    else
                    {
                        double guideY = guide.Position / ViewModel.CanvasHeight;
                        if (Math.Abs(newY - guideY) < thresholdY) newY = guideY;
                        if (Math.Abs((newY + _draggedSlot.Height / 2) - guideY) < thresholdY)
                            newY = guideY - _draggedSlot.Height / 2;
                    }
                }

                foreach (var other in ViewModel.Slots)
                {
                    if (other == _draggedSlot) continue;

                    if (Math.Abs(newX - other.X) < thresholdX) newX = other.X;
                    if (Math.Abs((newX + _draggedSlot.Width / 2) - (other.X + other.Width / 2)) < thresholdX)
                        newX = (other.X + other.Width / 2) - _draggedSlot.Width / 2;

                    if (Math.Abs(newY - other.Y) < thresholdY) newY = other.Y;
                    if (Math.Abs((newY + _draggedSlot.Height / 2) - (other.Y + other.Height / 2)) < thresholdY)
                        newY = (other.Y + other.Height / 2) - _draggedSlot.Height / 2;
                }
            }

            newX = Math.Max(0, Math.Min(1 - _draggedSlot.Width, newX));
            newY = Math.Max(0, Math.Min(1 - _draggedSlot.Height, newY));

            _draggedSlot.X = (float)newX;
            _draggedSlot.Y = (float)newY;
        }

        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingGuide)
            {
                _isDraggingGuide = false;
                _draggedGuide = null;
                Mouse.OverrideCursor = null;
                TemplateCanvas.ReleaseMouseCapture();
            }
            else
            {
                ResetDragState();
            }
        }

        private void InsertDesign_PreviewMouseDown(object sender, MouseButtonEventArgs e) { }

        private void InsertDesign_Click(object sender, RoutedEventArgs e)
        {
            // Command binding on the button executes LoadTemplateCommand automatically.
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

        private void GuideLine_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Line line && line.Tag is GuideLine guide && ViewModel != null)
            {
                _draggedGuide = guide;
                _isDraggingGuide = true;
                _dragStartPoint = e.GetPosition(TemplateCanvas);
                Mouse.OverrideCursor = Cursors.SizeAll;
                TemplateCanvas.CaptureMouse();
                e.Handled = true;
            }
        }

        private void GuideLine_RightClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is Line line && line.Tag is GuideLine guide && ViewModel != null)
            {
                ViewModel.Guides.Remove(guide);
                e.Handled = true;
            }
        }

        private void ColorSwatch_Click(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel?.SelectedSlot == null) return;

            var dialog = new ColorPaletteWindow(ViewModel.SelectedSlot.FontColor ?? "#000000");
            if (dialog.ShowDialog() == true)
                ViewModel.SelectedSlot.FontColor = dialog.SelectedColor;
        }

        private void PrintScaleMode_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton radioButton && ViewModel != null)
            {
                if (radioButton.Content.ToString()?.Contains("الحجم الفعلي") == true)
                    ViewModel.PrintScaleMode = Apex.NumberedBooksEngine.Core.PrintScaleMode.ActualSize;
                else if (radioButton.Content.ToString()?.Contains("ملاءمة") == true)
                    ViewModel.PrintScaleMode = Apex.NumberedBooksEngine.Core.PrintScaleMode.FitToPage;
            }
        }
    }
}
