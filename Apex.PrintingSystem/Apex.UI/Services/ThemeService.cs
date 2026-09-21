using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Apex.UI.Services
{
    /// <summary>
    /// Light or dark. The light palette is Styles/Brand.xaml, always loaded; dark mode
    /// merges Styles/Palette.Dark.xaml after it, which re-declares the same colour keys
    /// with dark values. Every screen reads its colours by name through DynamicResource,
    /// so adding or removing that one dictionary re-colours the running app at once —
    /// the same way the language switch works.
    /// </summary>
    public sealed class ThemeService
    {
        public static ThemeService Instance { get; } = new();

        /// <summary>Settings key the choice is stored under ("light" or "dark").</summary>
        public const string ThemeSettingKey = "UiTheme";

        private static readonly Uri DarkPalette =
            new("pack://application:,,,/Apex.UI;component/Styles/Palette.Dark.xaml", UriKind.Absolute);

        public bool IsDark { get; private set; }

        public event EventHandler? ThemeChanged;

        private ThemeService() { }

        /// <summary>Apply "light" or "dark". Anything else is treated as light.</summary>
        public void Apply(string? theme)
        {
            bool dark = string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase);
            var app = Application.Current;
            if (app == null) return;

            var merged = app.Resources.MergedDictionaries;
            var existing = merged.FirstOrDefault(d => d.Source != null &&
                d.Source.OriginalString.EndsWith("Palette.Dark.xaml", StringComparison.OrdinalIgnoreCase));

            if (dark && existing == null)
            {
                // Right after the palette, before the control styles: a key found in a
                // later merged dictionary wins, so it has to come after Brand.xaml.
                int brand = merged.ToList().FindIndex(d => d.Source != null &&
                    d.Source.OriginalString.EndsWith("Brand.xaml", StringComparison.OrdinalIgnoreCase));
                var dict = new ResourceDictionary { Source = DarkPalette };
                if (brand >= 0) merged.Insert(brand + 1, dict); else merged.Add(dict);
            }
            else if (!dark && existing != null)
            {
                merged.Remove(existing);
            }

            IsDark = dark;
            foreach (Window w in app.Windows)
                ApplyTitleBar(w);

            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Windows draws the title bar itself, so it stays white over a dark window
        /// unless it is asked for the dark frame. Harmless where unsupported.
        /// </summary>
        public void ApplyTitleBar(Window window)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero)
                {
                    // Not shown yet: do it once the handle exists.
                    window.SourceInitialized -= OnSourceInitialized;
                    window.SourceInitialized += OnSourceInitialized;
                    return;
                }
                int on = IsDark ? 1 : 0;
                // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE (Windows 10 20H1+ / 11); 19 on older builds.
                if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
            }
            catch { /* cosmetic only */ }
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            if (sender is Window w)
            {
                w.SourceInitialized -= OnSourceInitialized;
                ApplyTitleBar(w);
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
