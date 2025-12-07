using Apex.Core.Models;
using Apex.Core.Interfaces;
using Apex.Data.Migrations;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Apex.Data
{
    public class DbInitializer
    {
        private readonly ApexDbContext _context;
        private readonly AutoMigrationService _migrationService;
        private readonly IDatabaseHealthService _healthService;

        public DbInitializer(ApexDbContext context, AutoMigrationService migrationService, IDatabaseHealthService healthService)
        {
            _context = context;
            _migrationService = migrationService;
            _healthService = healthService;
        }

        public async Task InitializeAsync()
        {
            // 1. Apply Migrations (Schema Update)
            await _migrationService.ApplyMigrationsAsync();

            // 2. Seed System Settings
            if (!await _context.Settings.AnyAsync())
            {
                var settings = new List<SystemSettings>
                {
                    new SystemSettings { Key = "CompanyName", Value = "Apex Printing Press" },
                    new SystemSettings { Key = "Language", Value = "en" },
                    new SystemSettings { Key = "Theme", Value = "Light" }
                };
                await _context.Settings.AddRangeAsync(settings);
                await _context.SaveChangesAsync();
            }
        }
    }
}
