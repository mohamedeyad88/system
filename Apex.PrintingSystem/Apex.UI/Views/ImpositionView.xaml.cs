using System.Windows.Controls;
using System.Windows.Input;
using Apex.UI.ViewModels;

namespace Apex.UI.Views
{
    /// <summary>
    /// The sheet preview is drawn by <see cref="Controls.ImpositionSheetPreview"/>,
    /// which binds straight to the ViewModel and repaints itself when the slot
    /// collection changes — so no visual-tree building is needed here.
    /// </summary>
    public partial class ImpositionView : UserControl
    {
        public ImpositionView()
        {
            InitializeComponent();
        }

        // Ctrl+Wheel zooms the rendered-sheet preview; without Ctrl the wheel
        // scrolls the panel as usual.
        private void ImpPreview_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            if (DataContext is ImpositionViewModel vm)
            {
                vm.ApplyPreviewWheelZoom(e.Delta);
                e.Handled = true;
            }
        }
    }
}
