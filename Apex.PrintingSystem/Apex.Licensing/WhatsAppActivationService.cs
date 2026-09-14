using System;
using System.Diagnostics;
using System.Text;

namespace Apex.Licensing
{
    /// <summary>
    /// Generates a WhatsApp deep-link URL that opens a pre-filled activation message
    /// to the support number, containing the user's Device ID.
    /// </summary>
    public static class WhatsAppActivationService
    {
        // Support WhatsApp number (international format, digits only)
        private const string SupportPhone = "201099088053";

        // The name the website sells under.
        private const string ProductName = "Apex Print";

        /// <summary>
        /// Returns a WhatsApp URL that can be opened with <see cref="Process.Start"/>.
        /// The URL pre-fills a message containing the Device ID.
        /// </summary>
        public static string BuildActivationUrl(string deviceDisplayId)
        {
            var message = BuildActivationMessage(deviceDisplayId);
            var encoded = Uri.EscapeDataString(message);
            return $"https://api.whatsapp.com/send?phone={SupportPhone}&text={encoded}";
        }

        /// <summary>Opens the WhatsApp activation URL via the OS default handler (browser).</summary>
        public static void OpenWhatsApp(string deviceDisplayId)
        {
            var url = BuildActivationUrl(deviceDisplayId);

            // IMPORTANT: never route the URL through `cmd /c start`. The URL contains
            // percent-encoded Arabic (e.g. %D9%85…) and cmd expands %VAR% tokens, which
            // corrupts the URL → Windows shows "no app associated with this file".
            // ShellExecute opens the URL with the default browser and preserves it intact.
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
                // Fallback: hand the URL to Explorer, which also uses the default handler.
                Process.Start(new ProcessStartInfo("explorer.exe", url) { UseShellExecute = true });
            }
        }

        // ──────────────────────────────────────────────────────────────────

        private static string BuildActivationMessage(string deviceDisplayId)
        {
            var sb = new StringBuilder();
            // Shops buy a serial now; asking for a "licence file" sent support down the old
            // manual path. The device id stays, for a rebind or an offline .apex.
            sb.AppendLine($"مرحبًا، عايز أشتري أو أفعّل برنامج {ProductName}.");
            sb.AppendLine();
            sb.AppendLine($"رقم الجهاز: {deviceDisplayId}");
            return sb.ToString().TrimEnd();
        }
    }
}
