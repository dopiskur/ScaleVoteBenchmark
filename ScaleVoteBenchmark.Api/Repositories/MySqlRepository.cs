using MySqlConnector;
using ScaleVoteBenchmark.Api.Interfaces;
using ScaleVoteBenchmark.Api.Models;

namespace ScaleVoteBenchmark.Api.Repositories
{
    /// <summary>
    /// Repository implementation for working with an Azure Database for
    /// MySQL database. All functions communicate exclusively through
    /// stored procedures, without direct SELECT, INSERT, UPDATE or
    /// DELETE queries.
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

        public VoteReport VoteReportGet()
        {
            using var connection = new MySqlConnection(connectionString);
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "VoteReportGet";
            cmd.CommandType = System.Data.CommandType.StoredProcedure;

            using var dr = cmd.ExecuteReader();
            var report = new VoteReport();

            if (dr.Read())
            {
                report.Yes = dr["Yes"] != DBNull.Value ? Convert.ToInt32(dr["Yes"]) : 0;
                report.No = dr["No"] != DBNull.Value ? Convert.ToInt32(dr["No"]) : 0;
                report.Total = dr["Total"] != DBNull.Value ? Convert.ToInt32(dr["Total"]) : 0;
                report.YesPercent = dr["YesPercent"] != DBNull.Value ? Convert.ToDecimal(dr["YesPercent"]) : 0;
                report.NoPercent = dr["NoPercent"] != DBNull.Value ? Convert.ToDecimal(dr["NoPercent"]) : 0;
            }

            return report;
        }

        /// <summary>
        /// Opens and immediately closes a connection to Azure Database
        /// for MySQL, without executing any query. The exception is
        /// intentionally not caught here; it is passed to the caller so
        /// the application's startup logic can print a clear error
        /// message.
        /// </summary>
        public void TestConnection()
        {
            using var connection = new MySqlConnection(connectionString);
            connection.Open();
        }
    }
}
