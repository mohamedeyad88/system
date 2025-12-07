using System;
using System.ComponentModel.DataAnnotations;

namespace Apex.Data.Migrations
{
    public class SchemaVersion
    {
        [Key]
        public int Id { get; set; }
        public int Version { get; set; }
        public DateTime AppliedUtc { get; set; } = DateTime.UtcNow;
        public string Description { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
