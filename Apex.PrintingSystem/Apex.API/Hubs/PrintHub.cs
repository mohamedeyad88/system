using Microsoft.AspNetCore.SignalR;

namespace Apex.API.Hubs
{
    /// <summary>
    /// SignalR hub for real-time print dashboard updates.
    /// Clients call SubscribeToPrinter/UnsubscribeFromPrinter to filter updates.
    /// Server pushes: OnJobUpdate, OnPrinterHealth, OnStatsUpdate.
    /// </summary>
    public class PrintHub : Hub
    {
        /// <summary>
        /// Client → Server: join a printer-specific group to receive targeted updates.
        /// </summary>
        public async Task SubscribeToPrinter(string printerName)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"printer:{printerName}");
        }

        /// <summary>
        /// Client → Server: leave a printer-specific group.
        /// </summary>
        public async Task UnsubscribeFromPrinter(string printerName)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"printer:{printerName}");
        }
    }
}
