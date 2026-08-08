using System.Windows.Controls;

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
    }
}
