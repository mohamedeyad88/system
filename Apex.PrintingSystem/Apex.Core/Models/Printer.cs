using Apex.Core.Enums;

namespace Apex.Core.Models
{
    public class Printer
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = "Local"; // 'Local' | 'Network' | 'Virtual'
        public string? PoolName { get; set; }
        public string? CapabilitiesJson { get; set; }
        public string Status { get; set; } = "Online"; // 'Online' | 'Offline' | 'Error'
        public DateTime? LastSeenUtc { get; set; }

        // Legacy/Compat
        public string IpAddress { get; set; } = string.Empty;
        public int Port { get; set; } = 9100;
        public bool IsActive { get; set; } = true;
    }
}
