using System;
using System.Windows;

namespace Apex.Services
{
    public interface IThemeService
    {
        void SetTheme(string themeName);
        void SetAccentColor(string colorCode);
        string CurrentTheme { get; }
    }

    public class ThemeService : IThemeService
    {
        public string CurrentTheme { get; private set; } = "Light";

        public void SetTheme(string themeName)
        {
            // Placeholder for actual ResourceDictionary swapping
            // In a real implementation, this would load Light.xaml or Dark.xaml
            CurrentTheme = themeName;
        }

        public void SetAccentColor(string colorCode)
        {
            // Placeholder for dynamic accent color
        }
    }
}
