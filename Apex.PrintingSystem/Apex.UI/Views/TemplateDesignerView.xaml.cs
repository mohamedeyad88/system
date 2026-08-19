using Apex.UI.ViewModels;
using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Apex.UI.Views
{
    public partial class TemplateDesignerView : UserControl
    {
        // ── Drag state ────────────────────────────────────────────────────────
        private CanvasSlotItem? _draggingSlot;
        private Point _dragOrigin;        // in SlotsListBox coordinates
        private double _slotStartLeft;     // Canvas.Left (pts) at drag start
        private double _slotStartTop;      // Canvas.Top  (pts) at drag start
        private bool _isDragStarted;     // true once movement threshold crossed
        private const double DragThresholdPx = 4.0;

        // ── Resize state ──────────────────────────────────────────────────────
        private CanvasSlotItem? _resizingSlot;
        private Point _resizeOrigin;
        private double _resizeStartLeft, _resizeStartTop;
        private double _resizeStartW, _resizeStartH;
        private string? _resizeEdge;        // "N","S","E","W","NE","NW","SE","SW"
        private Border[] _resizeHandles = Array.Empty<Border>();
        private const double HandleSizePx = 10.0;

        private static readonly string[] EdgeNames =
            { "NW", "N", "NE", "W", "E", "SW", "S", "SE" };

        // ── Constructor ───────────────────────────────────────────────────────
        public TemplateDesignerView()
        {
            InitializeComponent();

            BtnImport.Click  += BtnImport_Click;
            BtnUploadBg.Click += BtnUploadBg_Click;

            DataContextChanged += TemplateDesignerView_DataContextChanged;
            Loaded += (_, _) => SetupResizeHandles();

            // Keyboard shortcuts
            PreviewKeyDown += TemplateDesignerView_PreviewKeyDown;
        }

        private void TemplateDesignerView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not TemplateDesignerViewModel vm) return;
            if (!vm.IsDesignStep) return;

            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool focusOnTextBox = Keyboard.FocusedElement is System.Windows.Controls.TextBox;

            switch (e.Key)
            {
                case Key.Delete:
                    if (vm.HasSlot && vm.DeleteSlotCommand.CanExecute(null))
                    {
                        vm.DeleteSlotCommand.Execute(null);
                        e.Handled = true;
                    }
                    break;

                case Key.Escape:
                    vm.ClearMultiSelect();
                    vm.SelectedCanvasSlot = null;
                    e.Handled = true;
                    break;

                case Key.S when ctrl:
                    if (vm.SaveCurrentTemplateCommand.CanExecute(null))
                    {
                        vm.SaveCurrentTemplateCommand.Execute(null);
                        e.Handled = true;
                    }
                    break;

                case Key.Z when ctrl:
                    if (vm.UndoCommand.CanExecute(null))
                        vm.UndoCommand.Execute(null);
                    e.Handled = true;
                    break;

                case Key.Y when ctrl:
                    if (vm.RedoCommand.CanExecute(null))
                        vm.RedoCommand.Execute(null);
                    e.Handled = true;
                    break;

                // ── Zoom shortcuts ────────────────────────────────
                case Key.OemPlus when ctrl:
                case Key.Add when ctrl:
                    vm.ZoomInCommand.Execute(null);
                    e.Handled = true;
                    break;

                case Key.OemMinus when ctrl:
                case Key.Subtract when ctrl:
                    vm.ZoomOutCommand.Execute(null);
                    e.Handled = true;
                    break;

                case Key.D0 when ctrl:
                case Key.NumPad0 when ctrl:
                    vm.FitToWindowCommand.Execute(null);
                    e.Handled = true;
                    break;

                // ── Copy / Paste / Duplicate ──────────────────────
                case Key.C when ctrl && !focusOnTextBox:
                    vm.CopySlotsCommand.Execute(null);
                    e.Handled = true;
                    break;

                case Key.V when ctrl && !focusOnTextBox:
                    vm.PasteSlotsCommand.Execute(null);
                    e.Handled = true;
                    break;

                case Key.D when ctrl && !focusOnTextBox:
                    vm.DuplicateSlotsCommand.Execute(null);
                    e.Handled = true;
                    break;

                // ── Select all ────────────────────────────────────
                case Key.A when ctrl && !focusOnTextBox:
                    vm.SelectAllMulti();
                    e.Handled = true;
                    break;
            }
        }

        // ── Ctrl+MouseWheel zoom ──────────────────────────────────────────

        private void CanvasScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            if (DataContext is not TemplateDesignerViewModel vm) return;
            e.Handled = true;

            // Zoom toward the mouse pointer: keep the artboard point under the cursor
            // fixed. ZoomBoost drives a LayoutTransform, so the ScrollViewer's extent
            // grows with it and the scroll offset can hold the anchor in place.
            double oldZoom = vm.ZoomBoost;
            double newZoom = Math.Clamp(Math.Round(oldZoom + (e.Delta > 0 ? 0.25 : -0.25), 2), 0.25, 3.0);
            if (newZoom == oldZoom) return;

            var mouse = e.GetPosition(CanvasScrollViewer);
            double factor = newZoom / oldZoom;
            double anchorX = CanvasScrollViewer.HorizontalOffset + mouse.X;
            double anchorY = CanvasScrollViewer.VerticalOffset + mouse.Y;

            vm.ZoomBoost = newZoom;
            CanvasScrollViewer.UpdateLayout();
            CanvasScrollViewer.ScrollToHorizontalOffset(anchorX * factor - mouse.X);
            CanvasScrollViewer.ScrollToVerticalOffset(anchorY * factor - mouse.Y);
        }

        // ── Fit to window ─────────────────────────────────────────────────────

        /// <summary>
        /// Scales the artboard to the space actually available.
        ///
        /// The Viewbox around the page cannot do this itself: it sits inside a
        /// ScrollViewer, which measures its content with infinite height, so the
        /// Viewbox never learns how much room there is and leaves the page at its
        /// natural size — a business card marooned in a large empty canvas. Meanwhile
        /// "Fit" set the zoom to 1.0, which is 100%, not a fit. The real ratio has to
        /// be computed from the viewport.
        /// </summary>
        private void FitCanvasToViewport()
        {
            if (DataContext is not TemplateDesignerViewModel vm) return;
            if (!vm.HasTemplate) return;

            double pageW = vm.CanvasWidthDip;
            double pageH = vm.CanvasHeightDip;
            if (pageW <= 0 || pageH <= 0) return;

            // The Viewbox carries a 24px margin on every side.
            const double Margin = 48;
            double availW = CanvasScrollViewer.ViewportWidth - Margin;
            double availH = CanvasScrollViewer.ViewportHeight - Margin;
            if (availW <= 0 || availH <= 0) return;

            double fit = Math.Min(availW / pageW, availH / pageH);

            // Stay inside the same range the zoom buttons use, and round to their
            // step so the percentage readout matches what the buttons produce.
            fit = Math.Clamp(fit, 0.25, 3.0);
            vm.ZoomBoost = Math.Round(fit, 2);
        }

        private void BtnFit_Click(object sender, RoutedEventArgs e) => FitCanvasToViewport();

        // ── File dialogs ──────────────────────────────────────────────────────

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "استيراد قالب Apex",
                Filter = "Apex Template (*.apext)|*.apext|كل الملفات|*.*",
                Multiselect = false
            };
            if (dlg.ShowDialog() != true) return;
            if (DataContext is TemplateDesignerViewModel vm)
                vm.ImportTemplateCommand.Execute(dlg.FileName);
        }

        private void BtnUploadBg_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "اختر خلفية التصميم (صورة أو PDF)",
                // PDF is accepted as a design base: the first page is rasterised and set
                // as the background, so numbering / variable fields can be laid on top.
                Filter = "صور و PDF|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tiff;*.pdf|"
                       + "PDF|*.pdf|"
                       + "صور|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tiff|"
                       + "كل الملفات|*.*"
            };
            if (dlg.ShowDialog() != true) return;
            if (DataContext is TemplateDesignerViewModel vm)
                vm.SetBackgroundCommand.Execute(dlg.FileName);
        }

        // ── DataContext tracking ───────────────────────────────────────────────

        private void TemplateDesignerView_DataContextChanged(
            object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is TemplateDesignerViewModel oldVm)
                oldVm.PropertyChanged -= Vm_PropertyChanged;
            if (e.NewValue is TemplateDesignerViewModel newVm)
                newVm.PropertyChanged += Vm_PropertyChanged;
        }

        private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Re-position resize handles whenever the selection or step changes
            if (e.PropertyName is nameof(TemplateDesignerViewModel.SelectedCanvasSlot)
                                or nameof(TemplateDesignerViewModel.IsDesignStep))
            {
                var vm = DataContext as TemplateDesignerViewModel;
                UpdateResizeHandles(vm?.IsDesignStep == true ? vm.SelectedCanvasSlot : null);
            }

            // Fit a newly opened template to the canvas. Opening a business card and
            // finding it the size of a postage stamp in the middle of an empty area
            // is the first thing a customer sees of this screen.
            // Deferred to Loaded priority so the ScrollViewer has measured its viewport.
            if (e.PropertyName is nameof(TemplateDesignerViewModel.CanvasWidthDip)
                                or nameof(TemplateDesignerViewModel.CanvasHeightDip)
                                or nameof(TemplateDesignerViewModel.HasTemplate))
            {
                Dispatcher.BeginInvoke(new Action(FitCanvasToViewport),
                    System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        // ── Canvas drag ───────────────────────────────────────────────────────
        //
        // Strategy: threshold-based capture so a plain click still selects the
        // item.  Mouse capture on SlotsListBox starts only after the pointer
        // moves more than DragThresholdPx from the press point.
        //
        // Coordinate note: e.GetPosition(SlotsListBox) returns coordinates in
        // the ListBox's local space, which is identical to Canvas slot space
        // (both share CanvasWidthDip × CanvasHeightDip dimensions, no padding,
        // BorderThickness="0").  The Viewbox scale is transparent — WPF
        // transforms through it automatically in GetPosition().

        private void SlotsListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Walk up the hit-test tree to find the ListBoxItem that was clicked.
            var lbi = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
            if (lbi?.DataContext is not CanvasSlotItem slot) return;

            var vm = DataContext as TemplateDesignerViewModel;
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

            if (ctrl && vm != null)
            {
                // Ctrl+Click — toggle multi-select
                vm.ToggleMultiSelect(slot);
                // Also select in ListBox for visual highlight
                vm.SelectedCanvasSlot = slot;
                e.Handled = true;   // prevent ListBox from clearing selection
                return;
            }

            // Normal click — clear multi-select and allow single selection
            vm?.ClearMultiSelect();

            _draggingSlot = slot;
            _dragOrigin = e.GetPosition(SlotsListBox);
            _slotStartLeft = slot.Left;
            _slotStartTop = slot.Top;
            _isDragStarted = false;

            // Do NOT capture here — let the ListBox handle selection (MouseLeftButtonDown
            // bubbles up to the ListBox's item selection logic).
            e.Handled = false;
        }

        private void SlotsListBox_MouseMove(object sender, MouseEventArgs e)
        {
            if (_draggingSlot == null) return;
            if (e.LeftButton != MouseButtonState.Pressed) { EndDrag(); return; }

            var current = e.GetPosition(SlotsListBox);
            double dx = current.X - _dragOrigin.X;
            double dy = current.Y - _dragOrigin.Y;

            if (!_isDragStarted)
            {
                // Require a minimum movement to distinguish drag from click
                if (Math.Abs(dx) < DragThresholdPx && Math.Abs(dy) < DragThresholdPx)
                    return;

                _isDragStarted = true;
                (DataContext as TemplateDesignerViewModel)?.PushUndoBeforeDrag();
                SlotsListBox.CaptureMouse();
                SlotsListBox.Cursor = Cursors.SizeAll;
            }

            // Clamp so the slot cannot be dragged outside the page
            var vm = DataContext as TemplateDesignerViewModel;
            double maxLeft = (vm?.CanvasWidthDip ?? double.MaxValue) - _draggingSlot.Width;
            double maxTop = (vm?.CanvasHeightDip ?? double.MaxValue) - _draggingSlot.Height;

            double newLeft = Math.Max(0, Math.Min(maxLeft, _slotStartLeft + dx));
            double newTop = Math.Max(0, Math.Min(maxTop, _slotStartTop + dy));

            // Grid snap
            if (vm != null)
            {
                newLeft = vm.SnapDip(newLeft);
                newTop = vm.SnapDip(newTop);
            }

            _draggingSlot.SetPosition(newLeft, newTop);
            vm?.OnSlotDragPositionChanged(_draggingSlot);

            // Keep resize handles in sync while dragging
            UpdateResizeHandles(_draggingSlot);
        }

        private void SlotsListBox_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
            => EndDrag();

        private void SlotsListBox_LostMouseCapture(object sender, MouseEventArgs e)
        {
            // Reset cursor and state whenever capture is lost (e.g. Escape / window deactivation)
            SlotsListBox.Cursor = Cursors.Arrow;
            _draggingSlot = null;
            _isDragStarted = false;
        }

        private void EndDrag()
        {
            if (_isDragStarted)
            {
                SlotsListBox.ReleaseMouseCapture();
                SlotsListBox.Cursor = Cursors.Arrow;
                // Mark dirty after a completed drag
                (DataContext as TemplateDesignerViewModel)?.NotifyDragDirty();
            }
            _draggingSlot = null;
            _isDragStarted = false;
        }

        // ── Resize handles ────────────────────────────────────────────────────
        //
        // 8 Border elements are created once in SetupResizeHandles() and added
        // to ResizeHandleCanvas (a transparent Canvas overlay above SlotsListBox
        // in the same page Grid).  UpdateResizeHandles() repositions them around
        // the selected slot; they are Collapsed when nothing is selected.
        //
        // Each handle captures its own mouse during a resize drag.  Coordinates
        // come from ResizeHandleCanvas.GetPosition(), which is in page-pts space
        // (same coordinate system as CanvasSlotItem.Left/Top/Width/Height).

        private static Cursor GetResizeCursor(string edge) => edge switch
        {
            "N" or "S" => Cursors.SizeNS,
            "E" or "W" => Cursors.SizeWE,
            "NW" or "SE" => Cursors.SizeNWSE,
            "NE" or "SW" => Cursors.SizeNESW,
            _ => Cursors.SizeAll,
        };

        private void SetupResizeHandles()
        {
            if (ResizeHandleCanvas == null) return;
            ResizeHandleCanvas.Children.Clear();
            _resizeHandles = new Border[8];

            for (int i = 0; i < 8; i++)
            {
                var h = new Border
                {
                    Width = HandleSizePx,
                    Height = HandleSizePx,
                    Background = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
                    BorderThickness = new Thickness(1.5),
                    CornerRadius = new CornerRadius(2),
                    Tag = EdgeNames[i],
                    Cursor = GetResizeCursor(EdgeNames[i]),
                    Visibility = Visibility.Collapsed,
                };
                h.PreviewMouseLeftButtonDown += ResizeHandle_MouseDown;
                h.MouseMove += ResizeHandle_MouseMove;
                h.MouseLeftButtonUp += ResizeHandle_MouseUp;
                h.LostMouseCapture += ResizeHandle_LostCapture;

                _resizeHandles[i] = h;
                ResizeHandleCanvas.Children.Add(h);
            }
        }

        /// <summary>
        /// Position the 8 resize handles around the given slot, or hide them all.
        /// Must be called whenever SelectedCanvasSlot or its geometry changes.
        /// </summary>
        private void UpdateResizeHandles(CanvasSlotItem? slot)
        {
            if (_resizeHandles.Length == 0) return;

            if (slot == null)
            {
                foreach (var h in _resizeHandles)
                    h.Visibility = Visibility.Collapsed;
                return;
            }

            double L = slot.Left;
            double T = slot.Top;
            double W = slot.Width;
            double H = slot.Height;
            double half = HandleSizePx / 2.0;

            // Order matches EdgeNames: NW, N, NE, W, E, SW, S, SE
            double[] xs = { L - half,        L + W / 2 - half,  L + W - half,
                             L - half,                           L + W - half,
                             L - half,        L + W / 2 - half,  L + W - half };
            double[] ys = { T - half,         T - half,          T - half,
                             T + H / 2 - half,                   T + H / 2 - half,
                             T + H - half,    T + H - half,      T + H - half };

            for (int i = 0; i < 8; i++)
            {
                Canvas.SetLeft(_resizeHandles[i], xs[i]);
                Canvas.SetTop(_resizeHandles[i], ys[i]);
                _resizeHandles[i].Visibility = Visibility.Visible;
            }
        }

        private void ResizeHandle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border handle) return;
            var vm = DataContext as TemplateDesignerViewModel;
            _resizingSlot = vm?.SelectedCanvasSlot;
            if (_resizingSlot == null) return;

            _resizeEdge = handle.Tag as string;
            _resizeOrigin = e.GetPosition(ResizeHandleCanvas);
            _resizeStartLeft = _resizingSlot.Left;
            _resizeStartTop = _resizingSlot.Top;
            _resizeStartW = _resizingSlot.Width;
            _resizeStartH = _resizingSlot.Height;
            vm?.PushUndoBeforeDrag();

            handle.CaptureMouse();
            e.Handled = true;
        }

        private void ResizeHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (_resizingSlot == null || _resizeEdge == null) return;
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndResize(sender as Border);
                return;
            }

            var vm = DataContext as TemplateDesignerViewModel;
            var current = e.GetPosition(ResizeHandleCanvas);
            double dx = current.X - _resizeOrigin.X;
            double dy = current.Y - _resizeOrigin.Y;

            const double MinSlotSize = 12.0;
            double newL = _resizeStartLeft, newT = _resizeStartTop;
            double newW = _resizeStartW, newH = _resizeStartH;

            // Each edge contributes to either position or size (or both)
            if (_resizeEdge.Contains('E')) newW = Math.Max(MinSlotSize, _resizeStartW + dx);
            if (_resizeEdge.Contains('S')) newH = Math.Max(MinSlotSize, _resizeStartH + dy);
            if (_resizeEdge.Contains('W'))
            {
                newW = Math.Max(MinSlotSize, _resizeStartW - dx);
                newL = _resizeStartLeft + (_resizeStartW - newW);   // anchor right edge
            }
            if (_resizeEdge.Contains('N'))
            {
                newH = Math.Max(MinSlotSize, _resizeStartH - dy);
                newT = _resizeStartTop + (_resizeStartH - newH);    // anchor bottom edge
            }

            // Grid snap
            if (vm != null)
            {
                newL = vm.SnapDip(newL);
                newT = vm.SnapDip(newT);
                newW = vm.SnapDip(newW);
                newH = vm.SnapDip(newH);
            }

            _resizingSlot.SetPosition(newL, newT);
            _resizingSlot.SetSize(newW, newH);

            // Live-update handle positions and the right-panel W/H inputs
            UpdateResizeHandles(_resizingSlot);
            vm?.OnSlotDragPositionChanged(_resizingSlot);
            vm?.OnSlotResizeChanged(_resizingSlot);
        }

        private void ResizeHandle_MouseUp(object sender, MouseButtonEventArgs e)
            => EndResize(sender as Border);

        private void ResizeHandle_LostCapture(object sender, MouseEventArgs e)
        {
            _resizingSlot = null;
            _resizeEdge = null;
        }

        private void EndResize(Border? handle)
        {
            if (_resizingSlot != null)
                (DataContext as TemplateDesignerViewModel)?.NotifyDragDirty();
            handle?.ReleaseMouseCapture();
            _resizingSlot = null;
            _resizeEdge = null;
        }

        // ── Visual-tree helpers ───────────────────────────────────────────────

        /// <summary>
        /// Walks up the visual tree from <paramref name="obj"/> and returns the first
        /// ancestor (or the element itself) that is of type <typeparamref name="T"/>.
        /// </summary>
        private static T? FindAncestor<T>(DependencyObject? obj) where T : DependencyObject
        {
            while (obj != null)
            {
                if (obj is T result) return result;
                obj = VisualTreeHelper.GetParent(obj);
            }
            return null;
        }
    }
}
