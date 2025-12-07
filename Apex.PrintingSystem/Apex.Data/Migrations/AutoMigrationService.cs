using System;
using System.Threading.Tasks;

namespace Apex.Data.Migrations
{
    public class AutoMigrationService
    {
        private readonly ApexDbContext _context;
        private readonly SchemaReader _schemaReader;
        private readonly ModelReader _modelReader;
        private readonly SchemaComparer _comparer;
        private readonly MigrationExecutor _executor;
        private readonly SchemaVersionRepository _versionRepo;

        public AutoMigrationService(ApexDbContext context)
        {
            _context = context;
            _schemaReader = new SchemaReader(context);
            _modelReader = new ModelReader(context);
            _comparer = new SchemaComparer();
            _versionRepo = new SchemaVersionRepository(context);
            // Planner needs model schema, but we can pass it later or init here if stateless
            // For now, we'll instantiate planner inside ApplyMigrationsAsync or pass null and init later
            // Actually, let's keep it simple and instantiate components that need state inside the method
            _executor = new MigrationExecutor(context, _versionRepo);
        }

        public async Task ApplyMigrationsAsync()
        {
            try
            {
                // 1. Ensure DB exists
                await _context.Database.EnsureCreatedAsync();
                await _versionRepo.EnsureTableExistsAsync();

                // 2. Read Schemas
                var dbSchema = await _schemaReader.ReadSchemaAsync();
                var modelSchema = _modelReader.ReadModel();

                // 3. Compare
                var steps = _comparer.Compare(dbSchema, modelSchema);

                if (steps.Count > 0)
                {
                    // 4. Plan
                    var planner = new MigrationPlanner(modelSchema);
                    planner.Plan(steps);

                    // 5. Execute
                    await _executor.ExecuteAsync(steps);
                }
            }
            catch (Exception ex)
            {
                // Repair Mode / Fallback logic could go here
                System.IO.File.AppendAllText("migration.log", $"[{DateTime.Now}] CRITICAL ERROR: {ex}\n");
                throw;
            }
        }
    }
}
