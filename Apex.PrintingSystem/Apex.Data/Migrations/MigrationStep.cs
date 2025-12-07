namespace Apex.Data.Migrations
{
    public enum MigrationType
    {
        CreateTable,
        AddColumn,
        AlterColumn, // Complex in SQLite
        DropColumn,  // Complex in SQLite
        Sql
    }

    public class MigrationStep
    {
        public MigrationType Type { get; set; }
        public string TableName { get; set; } = string.Empty;
        public string ColumnName { get; set; } = string.Empty; // Optional
        public string Sql { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
