using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;

namespace Apex.Data.Migrations
{
    public class SchemaReader
    {
        private readonly ApexDbContext _context;

        public SchemaReader(ApexDbContext context)
        {
            _context = context;
        }

        public async Task<DatabaseSchema> ReadSchemaAsync()
        {
            var schema = new DatabaseSchema();
            var connection = _context.Database.GetDbConnection();
            var wasOpen = connection.State == ConnectionState.Open;

            try
            {
                if (!wasOpen) await connection.OpenAsync();

                var tables = await GetTablesAsync(connection);
                foreach (var table in tables)
                {
                    var tableSchema = new TableSchema { Name = table };
                    tableSchema.Columns = await GetColumnsAsync(connection, table);
                    schema.Tables.Add(tableSchema);
                }
            }
            finally
            {
                if (!wasOpen) await connection.CloseAsync();
            }

            return schema;
        }

        private async Task<List<string>> GetTablesAsync(DbConnection connection)
        {
            var tables = new List<string>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '__EFMigrationsHistory'";
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        tables.Add(reader.GetString(0));
                    }
                }
            }
            return tables;
        }

        private async Task<List<ColumnSchema>> GetColumnsAsync(DbConnection connection, string tableName)
        {
            var columns = new List<ColumnSchema>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"PRAGMA table_info({tableName})";
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        columns.Add(new ColumnSchema
                        {
                            Name = reader.GetString(1),
                            Type = reader.GetString(2),
                            IsNullable = !reader.GetBoolean(3),
                            DefaultValue = reader.IsDBNull(4) ? null : reader.GetValue(4).ToString(),
                            IsPrimaryKey = reader.GetBoolean(5)
                        });
                    }
                }
            }
            return columns;
        }
    }
}
