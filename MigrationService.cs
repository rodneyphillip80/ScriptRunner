using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace MigrationRunner
{
    /// <summary>
    /// Provides functionality to discover, execute, and log SQL migration scripts.
    /// Scripts are executed using the external sqlcmd utility which supports
    /// batch separators (GO statements). After each script is run a record is
    /// inserted into a tracking table to ensure idempotent execution.
    /// </summary>
    public class MigrationService
    {
        private readonly string _server;
        private readonly string _database;
        private readonly string _user;
        private readonly string _password;
        private readonly string _scriptsDirectory;

        public MigrationService(string server, string database, string user, string password, string scriptsDirectory)
        {
            _server = server;
            _database = database;
            _user = user;
            _password = password;
            _scriptsDirectory = scriptsDirectory;
        }

        /// <summary>
        /// Discovers SQL files in the configured scripts directory and executes
        /// them in lexical order. Skips any scripts that have already been
        /// applied successfully according to the MigrationHistory table.
        /// </summary>
        public async Task RunMigrationsAsync()
        {
            Console.WriteLine("🔍 Scanning for migration scripts in " + _scriptsDirectory);

            if (!Directory.Exists(_scriptsDirectory))
            {
                Console.WriteLine($"❌ Scripts directory not found: {_scriptsDirectory}");
                return;
            }

            var scripts = Directory.GetFiles(_scriptsDirectory, "*.sql");
            Array.Sort(scripts, StringComparer.OrdinalIgnoreCase);

            foreach (var scriptPath in scripts)
            {
                var scriptName = Path.GetFileName(scriptPath);
                if (await AlreadyAppliedAsync(scriptName))
                {
                    Console.WriteLine($"⏩ Skipping {scriptName} (already applied)");
                    continue;
                }

                Console.WriteLine($"🚀 Executing {scriptName}...");
                bool success = await ExecuteSqlCmdAsync(scriptPath);
                await LogMigrationResultAsync(scriptName, success);
            }
        }

        /// <summary>
        /// Determines whether a script has been applied by checking the
        /// MigrationHistory table. Creates the table if it does not exist.
        /// </summary>
        private async Task<bool> AlreadyAppliedAsync(string scriptName)
        {
            using var conn = new SqlConnection(GetConnectionString());
            await conn.OpenAsync();

            // Ensure the MigrationHistory table exists and then check for the script
            string sql = @"
IF OBJECT_ID('dbo.MigrationHistory', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MigrationHistory(
        ScriptName VARCHAR(255) PRIMARY KEY,
        AppliedOn DATETIME,
        Status VARCHAR(50),
        ErrorMessage NVARCHAR(MAX)
    );
END;
SELECT COUNT(1) FROM dbo.MigrationHistory WHERE ScriptName = @name AND Status = 'Success';";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@name", scriptName);
            var result = (int)await cmd.ExecuteScalarAsync();
            return result > 0;
        }

        /// <summary>
        /// Executes a SQL script using the external sqlcmd utility. Captures
        /// both standard output and error streams and returns true if the
        /// process exits with a zero exit code.
        /// </summary>
        private async Task<bool> ExecuteSqlCmdAsync(string scriptPath)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sqlcmd",
                Arguments = $"-S {_server} -U {_user} -P {_password} -d {_database} -i \"{scriptPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using var process = new Process { StartInfo = psi };
            process.Start();
            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                Console.WriteLine($"✅ {Path.GetFileName(scriptPath)} applied successfully.");
                if (!string.IsNullOrWhiteSpace(output))
                {
                    Console.WriteLine(output);
                }
                return true;
            }
            else
            {
                Console.WriteLine($"❌ Error applying {Path.GetFileName(scriptPath)}");
                if (!string.IsNullOrWhiteSpace(error))
                {
                    Console.WriteLine(error);
                }
                return false;
            }
        }

        /// <summary>
        /// Records the result of a migration script execution into the
        /// MigrationHistory table. Includes the script name, timestamp, status,
        /// and an error message placeholder if the script failed.
        /// </summary>
        private async Task LogMigrationResultAsync(string scriptName, bool success)
        {
            using var conn = new SqlConnection(GetConnectionString());
            await conn.OpenAsync();
            string sql = @"
INSERT INTO dbo.MigrationHistory (ScriptName, AppliedOn, Status, ErrorMessage)
VALUES (@n, GETDATE(), @s, @e);";
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@n", scriptName);
            cmd.Parameters.AddWithValue("@s", success ? "Success" : "Failed");
            cmd.Parameters.AddWithValue("@e", success ? string.Empty : "See container logs for details");
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Builds a SQL Server connection string from the provided parameters.
        /// Encrypt is enabled by default and server certificate validation is
        /// disabled for containerized environments.
        /// </summary>
        private string GetConnectionString() =>
            $"Server={_server};Database={_database};User Id={_user};Password={_password};Encrypt=True;TrustServerCertificate=True;";
    }
}