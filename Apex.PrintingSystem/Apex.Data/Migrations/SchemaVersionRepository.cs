using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;

namespace Apex.Data.Migrations
{
    public class SchemaVersionRepository
    {
        private readonly ApexDbContext _context;

        public SchemaVersionRepository(ApexDbContext context)
        {
            _context = context;
        }

        public async Task EnsureTableExistsAsync()
        {
            var connection = _context.Database.GetDbConnection();
            var wasOpen = connection.State == System.Data.ConnectionState.Open;
            if (!wasOpen) await connection.OpenAsync();

            try
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        CREATE TABLE IF NOT EXISTS ""__SchemaVersion"" (
                            ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                            ""Version"" INTEGER NOT NULL,
                            ""AppliedUtc"" TEXT NOT NULL,
                            ""Description"" TEXT,
                            ""Success"" INTEGER NOT NULL,
                            ""ErrorMessage"" TEXT
                        );";
                    await command.ExecuteNonQueryAsync();
                }
            }
            finally
            {
                if (!wasOpen) await connection.CloseAsync();
            }
        }

        public async Task<int> GetLatestVersionAsync()
        {
            await EnsureTableExistsAsync();

            // We use raw SQL to avoid EF model issues if the table isn't in the model snapshot yet
            var connection = _context.Database.GetDbConnection();
            var wasOpen = connection.State == System.Data.ConnectionState.Open;
            if (!wasOpen) await connection.OpenAsync();

            try
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT MAX(Version) FROM \"__SchemaVersion\" WHERE Success = 1";
                    var result = await command.ExecuteScalarAsync();
                    return result == DBNull.Value ? 0 : Convert.ToInt32(result);
                }
            }
            finally
            {
                if (!wasOpen) await connection.CloseAsync();
            }
        }

        public async Task LogMigrationAsync(int version, string description, bool success, string? error = null)
        {
            var connection = _context.Database.GetDbConnection();
            var wasOpen = connection.State == System.Data.ConnectionState.Open;
            if (!wasOpen) await connection.OpenAsync();

            try
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        INSERT INTO ""__SchemaVersion"" (Version, AppliedUtc, Description, Success, ErrorMessage)
                        VALUES (@v, @t, @d, @s, @e)";

                    var pV = command.CreateParameter(); pV.ParameterName = "@v"; pV.Value = version; command.Parameters.Add(pV);
                    var pT = command.CreateParameter(); pT.ParameterName = "@t"; pT.Value = DateTime.UtcNow.ToString("o"); command.Parameters.Add(pT);
                    var pD = command.CreateParameter(); pD.ParameterName = "@d"; pD.Value = (object)description ?? DBNull.Value; command.Parameters.Add(pD);
                    var pS = command.CreateParameter(); pS.ParameterName = "@s"; pS.Value = success ? 1 : 0; command.Parameters.Add(pS);
                    var pE = command.CreateParameter(); pE.ParameterName = "@e"; pE.Value = (object?)error ?? DBNull.Value; command.Parameters.Add(pE);

                    await command.ExecuteNonQueryAsync();
                }
            }
            finally
            {
                if (!wasOpen) await connection.CloseAsync();
            }
        }
    }
}
