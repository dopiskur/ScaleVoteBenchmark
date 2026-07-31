using Npgsql;
using ScaleVoteBenchmark.Api.Interfaces;
using ScaleVoteBenchmark.Api.Models;

namespace ScaleVoteBenchmark.Api.Repositories
{
    /// <summary>
    /// Repository implementation for working with an Azure Database for
    /// PostgreSQL database. All functions communicate exclusively through
    /// stored procedures/functions, without direct SELECT, INSERT, UPDATE
    /// or DELETE queries.
    /// </summary>
    public class PostgreSqlRepository : IRepository
    {
        private readonly string connectionString;

        public PostgreSqlRepository(string connectionString)
        {
            this.connectionString = connectionString;
        }

        public void VoteAdd(string option)
        {
            using var connection = new NpgsqlConnection(connectionString);
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "CALL vote_add(@option)";
            cmd.Parameters.AddWithValue("option", option);
            cmd.ExecuteNonQuery();
        }

        public VoteCounts VoteCountsGet()
        {
            using var connection = new NpgsqlConnection(connectionString);
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT yes_count, no_count FROM vote_counts_get()";

            using var dr = cmd.ExecuteReader();
            var counts = new VoteCounts();

            if (dr.Read())
            {
                counts.Yes = dr["yes_count"] != DBNull.Value ? Convert.ToInt32(dr["yes_count"]) : 0;
                counts.No = dr["no_count"] != DBNull.Value ? Convert.ToInt32(dr["no_count"]) : 0;
            }

            return counts;
        }

        /// <summary>
        /// Opens and immediately closes a connection to Azure Database
        /// for PostgreSQL, without executing any query. The exception is
        /// intentionally not caught here; it is passed to the caller so
        /// the application's startup logic can print a clear error
        /// message.
        /// </summary>
        public void TestConnection()
        {
            using var connection = new NpgsqlConnection(connectionString);
            connection.Open();
        }
    }
}
