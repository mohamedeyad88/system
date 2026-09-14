using System;
using System.Diagnostics;

namespace Apex.UI.Services
{
    /// <summary>
    /// Links from the app to apexprint.me.
    ///
    /// <para>Before this, the activation screen said "enter the serial you received after
    /// purchase" and offered no way to purchase: a shop whose trial had ended could only
    /// message support on WhatsApp. The pricing page is now one click away wherever the
    /// app talks about licences.</para>
    ///
    /// <para><c>src=app</c> tells the site the visit came from inside the program. Nothing
    /// identifying — no device id, no customer — goes in the URL.</para>
    /// </summary>
    public static class WebLinks
    {
        public const string Site = "https://apexprint.me";

        public static string PricingUrl(bool arabic, string reason) =>
            $"{Site}/{(arabic ? "ar" : "en")}/pricing?src=app&reason={Uri.EscapeDataString(reason)}";

        /// <summary>Opens the pricing page in the operator's language. Never throws.</summary>
        public static void OpenPricing(string reason)
        {
            var url = PricingUrl(LocalizationService.Instance.IsArabicActive, reason);
            try
            {
                // ShellExecute, never `cmd /c start`: cmd expands %xx and corrupts the URL.
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
                try { Process.Start(new ProcessStartInfo("explorer.exe", url) { UseShellExecute = true }); }
                catch { }
            }
        }
    }
}
