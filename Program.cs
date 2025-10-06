using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace MigrationRunner
{
    /// <summary>
    /// Entry point for the SQL migration runner. This console application loads
    /// configuration from an appsettings.json file and runs all SQL scripts
    /// contained in the configured ScriptsDirectory. It logs progress to
    /// standard output and catches any errors encountered during execution.
    /// </summary>
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("=== SQL Migration Runner ===");

            // Attempt to load configuration from file. If the file cannot be
            // read or deserialized, report the error and abort execution.
            AppConfig? config;
            try
            {
                var json = await File.ReadAllTextAsync("appsettings.json");
                config = JsonSerializer.Deserialize<AppConfig>(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to load configuration: {ex.Message}");
                return;
            }

            if (config == null)
            {
                Console.WriteLine("❌ Configuration file is empty or invalid.");
                return;
            }

            var service = new MigrationService(
                config.Connection.Server,
                config.Connection.Database,
                config.Connection.User,
                config.Connection.Password,
                config.ScriptsDirectory);

            try
            {
                await service.RunMigrationsAsync();
            }
            catch (Exception ex)
            {
                // Catch any unexpected exceptions so that container startup does
                // not crash silently. A more sophisticated application might
                // report these errors to an external logging system.
                Console.WriteLine($"❌ An unexpected error occurred: {ex.Message}");
            }

            Console.WriteLine("🏁 Migration process completed.");
        }

        /// <summary>
        /// Strongly typed representation of the configuration stored in
        /// appsettings.json. This includes connection details for the SQL
        /// Server as well as the directory where migration scripts are located.
        /// </summary>
        public class AppConfig
        {
            public ConnectionInfo Connection { get; set; } = new ConnectionInfo();
            public string ScriptsDirectory { get; set; } = "./Scripts";
        }

        /// <summary>
        /// Connection information used to build a SQL Server connection string.
        /// </summary>
        public class ConnectionInfo
        {
            public string Server { get; set; } = string.Empty;
            public string Database { get; set; } = string.Empty;
            public string User { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }
    }
}