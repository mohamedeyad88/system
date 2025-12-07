using Apex.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Apex.Data
{
    public class ApexDbContext : DbContext
    {
        public ApexDbContext(DbContextOptions<ApexDbContext> options) : base(options)
        {
        }

        public DbSet<Printer> Printers { get; set; }
        public DbSet<PrintJob> PrintJobs { get; set; }
        public DbSet<SystemSettings> Settings { get; set; }
        
        // Print Manager Tables
        public DbSet<SavedQueue> SavedQueues { get; set; }
        public DbSet<SavedQueueItem> SavedQueueItems { get; set; }
        public DbSet<RoutingRule> RoutingRules { get; set; }
        public DbSet<PrinterPool> PrinterPools { get; set; }
        public DbSet<JobHistory> JobHistory { get; set; }
        public DbSet<Migrations.SchemaVersion> SchemaVersions { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // PrintJob Configuration
            modelBuilder.Entity<PrintJob>()
                .HasIndex(p => p.Status);

            // SystemSettings Key should be unique
            modelBuilder.Entity<SystemSettings>()
                .HasIndex(s => s.Key)
                .IsUnique();

            // SavedQueue -> SavedQueueItems
            modelBuilder.Entity<SavedQueue>()
                .HasMany(q => q.Items)
                .WithOne(i => i.SavedQueue)
                .HasForeignKey(i => i.SavedQueueId)
                .OnDelete(DeleteBehavior.Cascade);

            // PrinterPool Name Unique
            modelBuilder.Entity<PrinterPool>()
                .HasIndex(p => p.Name)
                .IsUnique();
        }
    }
}
