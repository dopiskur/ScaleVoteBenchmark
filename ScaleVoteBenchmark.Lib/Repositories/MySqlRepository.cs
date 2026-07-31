using MySqlConnector;
using ScaleVoteBenchmark.Lib.Interfaces;
using ScaleVoteBenchmark.Lib.Models;

namespace ScaleVoteBenchmark.Lib.Repositories
{
    /// <summary>
    /// Implementacija repozitorija za rad s Azure Database for MySQL
    /// bazom podataka. Sve funkcije komuniciraju isključivo putem
    /// pohranjenih procedura, bez direktnih SELECT, INSERT, UPDATE ili
    /// DELETE upita.
    /// </summary>
    public class MySqlRepository : IRepository
    {
        private readonly string connectionString;

        public MySqlRepository(string connectionString)
        {
            this.connectionString = connectionString;
        }

        public void VoteAdd(string option)
        {
            using var connection = new MySqlConnection(connectionString);
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "VoteAdd";
            cmd.CommandType = System.Data.CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("pOption", option);
            cmd.ExecuteNonQuery();
        }

        public VoteCounts VoteCountsGet()
        {
            using var connection = new MySqlConnection(connectionString);
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
        /// Otvara i odmah zatvara konekciju prema Azure Database for MySQL,
        /// bez izvršavanja bilo kakvog upita. Iznimka se namjerno ne hvata
        /// ovdje, već se propušta pozivatelju kako bi startup logika
        /// aplikacije mogla ispisati jasnu poruku o grešci.
        /// </summary>
        public void TestConnection()
        {
            using var connection = new MySqlConnection(connectionString);
            connection.Open();
        }
    }
}
