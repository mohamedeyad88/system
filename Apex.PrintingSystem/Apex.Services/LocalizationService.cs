using System.Globalization;
using System.Threading;
using System.Windows;

namespace Apex.Services
{
    public interface ILocalizationService
    {
        void SetLanguage(string cultureCode);
        string CurrentLanguage { get; }
    }

    public class LocalizationService : ILocalizationService
    {
        public string CurrentLanguage { get; private set; } = "en-US";

        public void SetLanguage(string cultureCode)
        {
            var culture = new CultureInfo(cultureCode);
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            CurrentLanguage = cultureCode;

            // Update FlowDirection
            if (Application.Current?.MainWindow != null)
            {
                Application.Current.MainWindow.FlowDirection = culture.TextInfo.IsRightToLeft
                    ? FlowDirection.RightToLeft
                    : FlowDirection.LeftToRight;
            }
        }
    }
}
