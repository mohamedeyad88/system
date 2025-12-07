using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;

namespace Apex.UI.Services
{
    public class LocalizationService : INotifyPropertyChanged
    {
        private static LocalizationService _instance;
        public static LocalizationService Instance => _instance ??= new LocalizationService();

        public event PropertyChangedEventHandler? PropertyChanged;

        public CultureInfo CurrentCulture
        {
            get => Thread.CurrentThread.CurrentUICulture;
            set
            {
                if (!Equals(value, Thread.CurrentThread.CurrentUICulture))
                {
                    Thread.CurrentThread.CurrentUICulture = value;
                    Thread.CurrentThread.CurrentCulture = value;
                    
                    UpdateResourceDictionary(value);

                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentCulture)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FlowDirection)));
                }
            }
        }

        public FlowDirection FlowDirection => 
            CurrentCulture.TextInfo.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        public void SwitchLanguage(string cultureCode)
        {
            CurrentCulture = new CultureInfo(cultureCode);
        }

        /// <summary>
        /// Get a localized string from the resource files
        /// </summary>
        public string GetString(string key)
        {
            try
            {
                var resourceManager = Properties.Resources.ResourceManager;
                var localizedString = resourceManager.GetString(key, CurrentCulture);
                return localizedString ?? key; // Return key if translation not found
            }
            catch
            {
                return key; // Fallback to key if any error occurs
            }
        }

        private void UpdateResourceDictionary(CultureInfo culture)
        {
            var app = Application.Current;
            if (app == null) return;

            var dictName = culture.Name.StartsWith("ar") ? "Language.ar.xaml" : "Language.en.xaml";
            var uri = new Uri($"Resources/{dictName}", UriKind.Relative);

            var oldDict = app.Resources.MergedDictionaries.FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("Language."));
            if (oldDict != null)
            {
                app.Resources.MergedDictionaries.Remove(oldDict);
            }

            var newDict = new ResourceDictionary { Source = uri };
            app.Resources.MergedDictionaries.Add(newDict);
        }
    }
}
