using Microsoft.Data.SqlClient;
using ScaleVoteBenchmark.Api.Azure;
using ScaleVoteBenchmark.Api.Interfaces;
using ScaleVoteBenchmark.Api.Models;

namespace ScaleVoteBenchmark.Api.Repositories
{
    /// <summary>
    /// Implementacija repozitorija za rad s Microsoft SQL Server / Azure SQL
    /// bazom podataka. Sve funkcije komuniciraju isključivo putem
    /// pohranjenih procedura, bez direktnih SELECT, INSERT, UPDATE ili
    /// DELETE upita.
    /// </summary>
    public class MsSqlRepository : IRepository
    {
        private readonly string connectionString;
        private readonly bool useManagedIdentity;

        public MsSqlRepository(string connectionString, bool useManagedIdentity = false)
        {
            this.connectionString = connectionString;
            this.useManagedIdentity = useManagedIdentity;
        }

        private SqlConnection CreateConnection()
        {
            var connection = new SqlConnection(connectionString);

            // Ako je omogućen Managed Identity način rada, lozinka se ne
            // koristi iz connection stringa, već se pribavlja privremeni
            // access token putem Azure AD identiteta aplikacije.
            //
            // SIGURNOSNA NAPOMENA: kada je useManagedIdentity=false (default),
            // aplikacija se spaja klasičnim putem, korisničkim imenom i
            // lozinkom sadržanima u connection stringu (appsettings.json).
            // Ovaj način rada je namjerno zadržan radi jednostavnosti
            // lokalnog razvoja i scenarija izvan Azure okruženja, no manje
            // je siguran jer lozinka baze podataka ostaje u konfiguracijskoj
            // datoteci u čitljivom obliku. Za produkcijsko okruženje u
            // Azureu preporučuje se Managed Identity mod.
            if (useManagedIdentity)
            {
                connection.AccessToken = AzureSqlAuthProvider.GetAccessToken();
            }

            return connection;
        }

        public void VoteAdd(string option)
        {
            using var connection = CreateConnection();
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "VoteAdd";
            cmd.CommandType = System.Data.CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@Option", option);
            cmd.ExecuteNonQuery();
        }

        public VoteCounts VoteCountsGet()
        {
            using var connection = CreateConnection();
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "VoteCountsGet";
            cmd.CommandType = System.Data.CommandType.StoredProcedure;

            using var dr = cmd.ExecuteReader();
            var counts = new VoteCounts();

            if (dr.Read())
            {
                counts.Yes = dr["Yes"] != DBNull.Value ? Convert.ToInt32(dr["Yes"]) : 0;
                counts.No = dr["No"] != DBNull.Value ? Convert.ToInt32(dr["No"]) : 0;
            }

            return counts;
        }

        /// <summary>
        /// Otvara i odmah zatvara konekciju prema Azure SQL bazi, bez
        /// izvršavanja bilo kakvog upita. Iznimka se namjerno ne hvata
        /// ovdje, već se propušta pozivatelju kako bi startup logika
        /// aplikacije mogla ispisati jasnu poruku o grešci.
        /// </summary>
        public void TestConnection()
        {
            using var connection = CreateConnection();
            connection.Open();
        }
    }
}
