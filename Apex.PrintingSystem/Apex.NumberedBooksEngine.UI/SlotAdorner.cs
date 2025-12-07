using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Apex.NumberedBooksEngine.UI
{
    public class SlotAdorner : Adorner
    {
        private readonly Thumb _topLeft, _topRight, _bottomLeft, _bottomRight;
        private readonly Thumb _moveThumb;
        private readonly VisualCollection _visuals;
        private readonly Canvas _canvas;

        public SlotAdorner(UIElement adornedElement) : base(adornedElement)
        {
            _canvas = (Canvas)VisualTreeHelper.GetParent(adornedElement);
            _visuals = new VisualCollection(this);

            _moveThumb = CreateMoveThumb();
            _topLeft = CreateResizeThumb(Cursors.SizeNWSE, VerticalAlignment.Top, HorizontalAlignment.Left);
            _topRight = CreateResizeThumb(Cursors.SizeNESW, VerticalAlignment.Top, HorizontalAlignment.Right);
            _bottomLeft = CreateResizeThumb(Cursors.SizeNESW, VerticalAlignment.Bottom, HorizontalAlignment.Left);
            _bottomRight = CreateResizeThumb(Cursors.SizeNWSE, VerticalAlignment.Bottom, HorizontalAlignment.Right);

            _visuals.Add(_moveThumb);
            _visuals.Add(_topLeft);
            _visuals.Add(_topRight);
            _visuals.Add(_bottomLeft);
            _visuals.Add(_bottomRight);
        }

        private Thumb CreateMoveThumb()
        {
            var thumb = new Thumb
            {
                Cursor = Cursors.SizeAll,
                Template = new ControlTemplate(typeof(Thumb))
                {
                    VisualTree = GetMoveThumbTemplate()
                },
                Opacity = 0 // Invisible, covers the whole element
            };
            thumb.DragDelta += MoveThumb_DragDelta;
            return thumb;
        }

        private FrameworkElementFactory GetMoveThumbTemplate()
        {
            var factory = new FrameworkElementFactory(typeof(Rectangle));
            factory.SetValue(Shape.FillProperty, Brushes.Transparent);
            return factory;
        }

        private Thumb CreateResizeThumb(Cursor cursor, VerticalAlignment verticalAlignment, HorizontalAlignment horizontalAlignment)
        {
            var thumb = new Thumb
            {
                Cursor = cursor,
                Width = 10,
                Height = 10,
                VerticalAlignment = verticalAlignment,
                HorizontalAlignment = horizontalAlignment,
                Background = Brushes.White,
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1)
            };
            thumb.DragDelta += ResizeThumb_DragDelta;
            return thumb;
        }

        private void MoveThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            var element = (FrameworkElement)AdornedElement;
            double left = Canvas.GetLeft(element);
            double top = Canvas.GetTop(element);

            double newLeft = left + e.HorizontalChange;
            double newTop = top + e.VerticalChange;

            // Boundary checks
            if (newLeft < 0) newLeft = 0;
            if (newTop < 0) newTop = 0;
            if (newLeft + element.Width > _canvas.ActualWidth) newLeft = _canvas.ActualWidth - element.Width;
            if (newTop + element.Height > _canvas.ActualHeight) newTop = _canvas.ActualHeight - element.Height;

            Canvas.SetLeft(element, newLeft);
            Canvas.SetTop(element, newTop);
        }

        private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            var element = (FrameworkElement)AdornedElement;
            var thumb = (Thumb)sender;

            double left = Canvas.GetLeft(element);
            double top = Canvas.GetTop(element);
            double width = element.Width;
            double height = element.Height;

            if (thumb.VerticalAlignment == VerticalAlignment.Bottom)
            {
                height += e.VerticalChange;
            }
            else if (thumb.VerticalAlignment == VerticalAlignment.Top)
            {
                double newTop = top + e.VerticalChange;
                if (newTop >= 0 && height - e.VerticalChange > 0)
                {
                    top = newTop;
                    height -= e.VerticalChange;
                }
            }

            if (thumb.HorizontalAlignment == HorizontalAlignment.Right)
            {
                width += e.HorizontalChange;
            }
            else if (thumb.HorizontalAlignment == HorizontalAlignment.Left)
            {
                double newLeft = left + e.HorizontalChange;
                if (newLeft >= 0 && width - e.HorizontalChange > 0)
                {
                    left = newLeft;
                    width -= e.HorizontalChange;
                }
            }

            if (width > 10) element.Width = width;
            if (height > 10) element.Height = height;
            
            Canvas.SetLeft(element, left);
            Canvas.SetTop(element, top);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            _moveThumb.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
            
            _topLeft.Arrange(new Rect(-5, -5, 10, 10));
            _topRight.Arrange(new Rect(finalSize.Width - 5, -5, 10, 10));
            _bottomLeft.Arrange(new Rect(-5, finalSize.Height - 5, 10, 10));
            _bottomRight.Arrange(new Rect(finalSize.Width - 5, finalSize.Height - 5, 10, 10));

            return finalSize;
        }

        protected override Visual GetVisualChild(int index) => _visuals[index];
        protected override int VisualChildrenCount => _visuals.Count;
    }
}
