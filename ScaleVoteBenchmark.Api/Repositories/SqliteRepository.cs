using Microsoft.Data.Sqlite;
using ScaleVoteBenchmark.Api.Interfaces;
using ScaleVoteBenchmark.Api.Models;
using ScaleVoteBenchmark.Api.Schema;

namespace ScaleVoteBenchmark.Api.Repositories
{
    /// <summary>
    /// Repository implementation for working with a local SQLite database
    /// file. Intended for local development and testing without needing
    /// a real Azure SQL/MySQL/PostgreSQL server. SQLite has no stored
    /// procedure/function support, so unlike the other three
    /// repositories this one runs plain parameterized SQL directly
    /// instead of calling stored routines.
    /// </summary>
    public class SqliteRepository : IRepository
    {
        private readonly string connectionString;

        public SqliteRepository(string connectionString)
        {
            this.connectionString = connectionString;
        }

        public void VoteAdd(string option)
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO Vote (\"Option\") VALUES ($option)";
            cmd.Parameters.AddWithValue("$option", option);
            cmd.ExecuteNonQuery();
        }

        public VoteReport VoteReportGet()
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText =
                "SELECT " +
                "SUM(CASE WHEN \"Option\" = 'yes' THEN 1 ELSE 0 END) AS Yes, " +
                "SUM(CASE WHEN \"Option\" = 'no'  THEN 1 ELSE 0 END) AS No, " +
                "COUNT(*) AS Total, " +
                "CASE WHEN COUNT(*) = 0 THEN 0 " +
                "     ELSE ROUND(100.0 * SUM(CASE WHEN \"Option\" = 'yes' THEN 1 ELSE 0 END) / COUNT(*), 2) " +
                "END AS YesPercent, " +
                "CASE WHEN COUNT(*) = 0 THEN 0 " +
                "     ELSE ROUND(100.0 * SUM(CASE WHEN \"Option\" = 'no' THEN 1 ELSE 0 END) / COUNT(*), 2) " +
                "END AS NoPercent " +
                "FROM Vote";

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
        /// Opens and immediately closes a connection to the SQLite
        /// database file. SQLite creates the file automatically on first
        /// connection if it does not yet exist, so this effectively
        /// always succeeds as long as the configured path is writable.
        /// </summary>
        public void TestConnection()
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
        }

        public void EnsureSchema()
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            using (var checkCmd = connection.CreateCommand())
            {
                checkCmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'Vote'";
                if (checkCmd.ExecuteScalar() != null)
                {
                    return;
                }
            }

            foreach (var batch in SchemaScripts.Sqlite)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = batch;
                cmd.ExecuteNonQuery();
            }
        }
    }
}
