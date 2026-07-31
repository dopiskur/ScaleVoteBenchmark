using Microsoft.Data.SqlClient;
using ScaleVoteBenchmark.Lib.Azure;
using ScaleVoteBenchmark.Lib.Interfaces;
using ScaleVoteBenchmark.Lib.Models;

namespace ScaleVoteBenchmark.Lib.Repositories
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
                counts.Pas = dr["Pas"] != DBNull.Value ? Convert.ToInt32(dr["Pas"]) : 0;
                counts.Macka = dr["Macka"] != DBNull.Value ? Convert.ToInt32(dr["Macka"]) : 0;
            }

            return counts;
        }
    }
}
