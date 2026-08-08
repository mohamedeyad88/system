using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Apex.Data.Migrations
{
    public class MigrationPlanner
    {
        private readonly DatabaseSchema _modelSchema;

        public MigrationPlanner(DatabaseSchema modelSchema)
        {
            _modelSchema = modelSchema;
        }

        public void Plan(List<MigrationStep> steps)
        {
            foreach (var step in steps)
            {
                if (step.Type == MigrationType.CreateTable)
                {
                    step.Sql = GenerateCreateTableSql(step.TableName);
                }
                else if (step.Type == MigrationType.AddColumn)
                {
                    step.Sql = GenerateAddColumnSql(step.TableName, step.ColumnName);
                }
            }
        }

        private string GenerateCreateTableSql(string tableName)
        {
            var table = _modelSchema.Tables.First(t => t.Name == tableName);
            var sb = new StringBuilder();
            sb.AppendLine($"CREATE TABLE \"{tableName}\" (");

            var colDefs = new List<string>();
            foreach (var col in table.Columns)
            {
                var def = $"\"{col.Name}\" {col.Type}";
                if (col.IsPrimaryKey) def += " PRIMARY KEY AUTOINCREMENT"; // Assuming int PK for simplicity
                else if (!col.IsNullable) def += " NOT NULL";

                if (col.DefaultValue != null) def += $" DEFAULT {col.DefaultValue}";

                colDefs.Add(def);
            }

            sb.Append(string.Join(",\n", colDefs));
            sb.Append(");");

            return sb.ToString();
        }

        private string GenerateAddColumnSql(string tableName, string columnName)
        {
            var table = _modelSchema.Tables.First(t => t.Name == tableName);
            var col = table.Columns.First(c => c.Name == columnName);

            var def = $"\"{col.Name}\" {col.Type}";
            if (!col.IsNullable) def += " DEFAULT ''"; // Add default for non-null new columns to avoid error

            return $"ALTER TABLE \"{tableName}\" ADD COLUMN {def};";
        }
    }
}
