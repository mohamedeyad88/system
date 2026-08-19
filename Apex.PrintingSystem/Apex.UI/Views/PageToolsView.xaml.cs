using System.Windows.Controls;
using System.Windows.Input;
using Apex.UI.ViewModels;

namespace Apex.UI.Views
{
    /// <summary>Pre-press "Page Tools" screen.</summary>
    public partial class PageToolsView : UserControl
    {
        public PageToolsView()
        {
            InitializeComponent();
        }

        // Ctrl+Wheel zooms the preview; without Ctrl the wheel scrolls/pans as usual.
        private void PtPreview_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            if (DataContext is PageToolsViewModel vm)
            {
                vm.ApplyPreviewWheelZoom(e.Delta);
                e.Handled = true;
            }
        }
    }
}
