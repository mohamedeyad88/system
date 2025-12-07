using System;
using System.Collections.Generic;
using System.Linq;

namespace Apex.Data.Migrations
{
    public class SchemaComparer
    {
        public List<MigrationStep> Compare(DatabaseSchema dbSchema, DatabaseSchema modelSchema)
        {
            var steps = new List<MigrationStep>();

            // 1. Check for missing tables
            foreach (var modelTable in modelSchema.Tables)
            {
                var dbTable = dbSchema.Tables.FirstOrDefault(t => t.Name.Equals(modelTable.Name, StringComparison.OrdinalIgnoreCase));
                if (dbTable == null)
                {
                    // Table missing, create it
                    steps.Add(new MigrationStep
                    {
                        Type = MigrationType.CreateTable,
                        TableName = modelTable.Name,
                        Description = $"Create table {modelTable.Name}"
                    });
                }
                else
                {
                    // Table exists, check columns
                    foreach (var modelCol in modelTable.Columns)
                    {
                        var dbCol = dbTable.Columns.FirstOrDefault(c => c.Name.Equals(modelCol.Name, StringComparison.OrdinalIgnoreCase));
                        if (dbCol == null)
                        {
                            // Column missing
                            steps.Add(new MigrationStep
                            {
                                Type = MigrationType.AddColumn,
                                TableName = modelTable.Name,
                                ColumnName = modelCol.Name,
                                Description = $"Add column {modelCol.Name} to {modelTable.Name}"
                            });
                        }
                        else
                        {
                            // Column exists, check type (simplified)
                            // SQLite types are flexible, but we might want to warn or migrate if drastically different
                            // For now, we skip type migration as it's complex in SQLite (requires table rebuild)
                        }
                    }
                }
            }

            return steps;
        }
    }
}
