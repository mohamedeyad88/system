using System;
using System.Collections.Generic;

namespace Apex.Core.Models
{
    public class SavedQueue
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? CreatedBy { get; set; }
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public virtual ICollection<SavedQueueItem> Items { get; set; } = new List<SavedQueueItem>();
    }

    public class SavedQueueItem
    {
        public int Id { get; set; }
        public int SavedQueueId { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public string? OptionsJson { get; set; }

        public virtual SavedQueue SavedQueue { get; set; } = null!;
    }
}
