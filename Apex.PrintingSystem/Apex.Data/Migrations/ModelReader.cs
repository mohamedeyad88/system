using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Linq;

namespace Apex.Data.Migrations
{
    public class ModelReader
    {
        private readonly ApexDbContext _context;

        public ModelReader(ApexDbContext context)
        {
            _context = context;
        }

        public DatabaseSchema ReadModel()
        {
            var schema = new DatabaseSchema();
            var model = _context.Model;

            foreach (var entityType in model.GetEntityTypes())
            {
                // Skip shadow types or views if any
                if (entityType.FindPrimaryKey() == null) continue;

                var tableName = entityType.GetTableName();
                if (string.IsNullOrEmpty(tableName)) continue;

                var tableSchema = new TableSchema { Name = tableName };

                foreach (var property in entityType.GetProperties())
                {
                    var columnName = property.GetColumnName();
                    var columnType = property.GetColumnType();

                    // Map C# types to SQLite types if GetColumnType returns null or C# type
                    if (string.IsNullOrEmpty(columnType))
                    {
                        columnType = MapToSqliteType(property.ClrType);
                    }

                    tableSchema.Columns.Add(new ColumnSchema
                    {
                        Name = columnName,
                        Type = columnType,
                        IsNullable = property.IsNullable,
                        IsPrimaryKey = property.IsPrimaryKey()
                    });
                }

                schema.Tables.Add(tableSchema);
            }

            return schema;
        }

        private string MapToSqliteType(System.Type type)
        {
            if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(bool))
                return "INTEGER";
            if (type == typeof(string) || type == typeof(System.Guid) || type == typeof(System.DateTime))
                return "TEXT";
            if (type == typeof(decimal) || type == typeof(double) || type == typeof(float))
                return "REAL";
            if (type == typeof(byte[]))
                return "BLOB";

            // Nullable types
            if (System.Nullable.GetUnderlyingType(type) != null)
                return MapToSqliteType(System.Nullable.GetUnderlyingType(type)!);

            return "TEXT"; // Default
        }
    }
}
