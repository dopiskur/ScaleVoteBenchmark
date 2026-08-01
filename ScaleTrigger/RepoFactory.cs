using ScaleTrigger.Interfaces;
using ScaleTrigger.Repositories;

namespace ScaleTrigger
{
    public class RepoFactory
    {
        private readonly IConfiguration configuration;
        private readonly ICache cache;

        public RepoFactory(IConfiguration configuration, ICache cache)
        {
            this.configuration = configuration;
            this.cache = cache;
        }

        public IRepository GetRepo()
        {
            string provider = configuration["DatabaseProvider"] ?? "Sqlite";

            return provider switch
            {
                "MySql" => new MySqlRepository(configuration.GetConnectionString("MySql")
                    ?? throw new InvalidOperationException("Missing connection string 'MySql' in appsettings.json.")),

                "PostgreSql" => new PostgreSqlRepository(configuration.GetConnectionString("PostgreSql")
                    ?? throw new InvalidOperationException("Missing connection string 'PostgreSql' in appsettings.json.")),

                "Sqlite" => new SqliteRepository(configuration.GetConnectionString("Sqlite")
                    ?? throw new InvalidOperationException("Missing connection string 'Sqlite' in appsettings.json.")),

                "MsSql" => new MsSqlRepository(
                    configuration.GetConnectionString("MsSql")
                        ?? throw new InvalidOperationException("Missing connection string 'MsSql' in appsettings.json."),
                    useManagedIdentity: bool.TryParse(configuration["UseManagedIdentity"], out bool umi) && umi),

                _ => throw new InvalidOperationException($"Unknown value for the DatabaseProvider setting: '{provider}'.")
            };
        }

        public ICache GetCache() => cache;
    }
}
