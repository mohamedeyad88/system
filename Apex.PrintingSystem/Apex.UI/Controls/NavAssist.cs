using System.Windows;
using System.Windows.Media;

namespace Apex.UI.Controls
{
    /// <summary>
    /// Attached properties consumed by the shared sidebar nav-button template
    /// (<c>NavItemButton</c> in Styles/Controls.xaml): per-item emoji icon, icon
    /// gradient brush, and active-state flag. Replaces 11 duplicated ~55-line
    /// inline ControlTemplates with one shared template.
    /// </summary>
    public static class NavAssist
    {
        public static readonly DependencyProperty IconProperty =
            DependencyProperty.RegisterAttached(
                "Icon", typeof(string), typeof(NavAssist), new PropertyMetadata(string.Empty));

        public static string GetIcon(DependencyObject obj) => (string)obj.GetValue(IconProperty);
        public static void SetIcon(DependencyObject obj, string value) => obj.SetValue(IconProperty, value);

        public static readonly DependencyProperty IconBrushProperty =
            DependencyProperty.RegisterAttached(
                "IconBrush", typeof(Brush), typeof(NavAssist), new PropertyMetadata(null));

        public static Brush? GetIconBrush(DependencyObject obj) => (Brush?)obj.GetValue(IconBrushProperty);
        public static void SetIconBrush(DependencyObject obj, Brush? value) => obj.SetValue(IconBrushProperty, value);

        public static readonly DependencyProperty IsActiveProperty =
            DependencyProperty.RegisterAttached(
                "IsActive", typeof(bool), typeof(NavAssist), new PropertyMetadata(false));

        public static bool GetIsActive(DependencyObject obj) => (bool)obj.GetValue(IsActiveProperty);
        public static void SetIsActive(DependencyObject obj, bool value) => obj.SetValue(IsActiveProperty, value);
    }
}
