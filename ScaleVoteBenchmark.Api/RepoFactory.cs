using ScaleVoteBenchmark.Api.Interfaces;
using ScaleVoteBenchmark.Api.Repositories;

namespace ScaleVoteBenchmark.Api
{
    /// <summary>
    /// Tvornička klasa koja, na temelju postavke "DatabaseProvider" u
    /// appsettings.json datoteci, vraća ispravnu implementaciju
    /// repozitorija (MsSql ili MySql), bez potrebe da se ostatak koda
    /// brine o tome koja se baza podataka koristi u pozadini.
    /// </summary>
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
            string provider = configuration["DatabaseProvider"] ?? "MsSql";

            return provider switch
            {
                "MySql" => new MySqlRepository(configuration.GetConnectionString("MySql")
                    ?? throw new InvalidOperationException("Nedostaje connection string 'MySql' u appsettings.json.")),

                "MsSql" => new MsSqlRepository(
                    configuration.GetConnectionString("MsSql")
                        ?? throw new InvalidOperationException("Nedostaje connection string 'MsSql' u appsettings.json."),
                    useManagedIdentity: bool.TryParse(configuration["UseManagedIdentity"], out bool umi) && umi),

                _ => throw new InvalidOperationException($"Nepoznata vrijednost postavke DatabaseProvider: '{provider}'.")
            };
        }

        public ICache GetCache() => cache;
    }
}
