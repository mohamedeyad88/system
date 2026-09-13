using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System;

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
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsArabicActive)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnglishActive)));
                }
            }
        }

        public FlowDirection FlowDirection =>
            CurrentCulture.TextInfo.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        public bool IsArabicActive => CurrentCulture.Name.StartsWith("ar");
        public bool IsEnglishActive => !CurrentCulture.Name.StartsWith("ar");

        /// <summary>
        /// Settings key for the chosen UI language ("ar" / "en"). Deliberately not the old
        /// "Language" key: Settings used to save that from a picker that showed "en" in Arabic
        /// sessions, so existing installs may hold an "en" nobody chose.
        /// </summary>
        public const string LanguageSettingKey = "UiLanguage";

        public void SwitchLanguage(string cultureCode)
        {
            // Normalize language codes: "en" -> "en-US", "ar" -> "ar" (or "ar-SA")
            string normalizedCode = cultureCode.ToLowerInvariant() switch
            {
                "en" => "en-US",
                "ar" => "ar",
                _ => cultureCode
            };

            CurrentCulture = new CultureInfo(normalizedCode);
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

            bool isArabic = culture.Name.StartsWith("ar");

            // Use Dispatcher to ensure UI thread safety
            app.Dispatcher.Invoke(() =>
            {
                try
                {
                    // Find the pre-loaded language dictionaries (loaded at startup in App.xaml)
                    var arDict = app.Resources.MergedDictionaries
                        .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("Language.ar"));
                    var enDict = app.Resources.MergedDictionaries
                        .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("Language.en"));

                    if (arDict != null && enDict != null)
                    {
                        // Remove both language dictionaries
                        app.Resources.MergedDictionaries.Remove(arDict);
                        app.Resources.MergedDictionaries.Remove(enDict);

                        // Re-add in correct order: target language LAST (last wins for duplicate keys)
                        if (isArabic)
                        {
                            app.Resources.MergedDictionaries.Add(enDict);
                            app.Resources.MergedDictionaries.Add(arDict);
                        }
                        else
                        {
                            app.Resources.MergedDictionaries.Add(arDict);
                            app.Resources.MergedDictionaries.Add(enDict);
                        }
                    }

                    // Update FlowDirection for all windows
                    var flowDirection = FlowDirection;

                    foreach (Window window in app.Windows)
                    {
                        window.FlowDirection = flowDirection;
                    }
                }
                catch (Exception ex)
                {
                    System.IO.File.AppendAllText("localization_error.log",
                        $"[{DateTime.Now}] Error switching language: {ex.Message}\n{ex.StackTrace}\n");
                }
            });
        }
    }
}
