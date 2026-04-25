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

        private const string ProductName  = "أبكس لحلول الطباعة المتكاملة";

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

        /// <summary>Opens the WhatsApp activation URL in the default browser.</summary>
        public static void OpenWhatsApp(string deviceDisplayId)
        {
            var url = BuildActivationUrl(deviceDisplayId);
            // Use cmd /c start to reliably open URLs on Windows
            Process.Start(new ProcessStartInfo("cmd", $"/c start \"\" \"{url}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }

        // ──────────────────────────────────────────────────────────────────

        private static string BuildActivationMessage(string deviceDisplayId)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"مرحبًا، أريد تفعيل برنامج {ProductName}.");
            sb.AppendLine();
            sb.AppendLine($"رقم الجهاز: {deviceDisplayId}");
            sb.AppendLine();
            sb.AppendLine("يرجى إرسال ملف الترخيص. شكرًا.");
            return sb.ToString().TrimEnd();
        }
    }
}
