using Apex.Core.Interfaces;
using Apex.Core.Models;
using Apex.Data;
using Apex.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Apex.UI.Modules
{
    public static class DataModule
    {
        public static IServiceCollection AddApexData(this IServiceCollection services)
        {
            // Database
            services.AddDbContext<ApexDbContext>(options =>
            {
                var dbPath = @"C:\ProgramData\ApexPrintingSystem\Database\apex.db";
                options.UseSqlite($"Data Source={dbPath}");
            });

            // Repositories
            services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
            services.AddScoped<IRepository<Printer>, PrinterRepository>();
            services.AddScoped<IRepository<PrintJob>, PrintJobRepository>();
            services.AddScoped<IRepository<SystemSettings>, SettingsRepository>();
            
            // Initialization
            services.AddScoped<Apex.Data.Migrations.AutoMigrationService>();
            services.AddScoped<DbInitializer>();

            return services;
        }
    }
}
