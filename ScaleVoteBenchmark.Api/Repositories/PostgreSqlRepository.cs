using Npgsql;
using ScaleVoteBenchmark.Api.Interfaces;
using ScaleVoteBenchmark.Api.Models;
using ScaleVoteBenchmark.Api.Schema;

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

        public async Task VoteAddAsync(string option)
        {
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "CALL vote_add(@option)";
            cmd.Parameters.AddWithValue("option", option);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<VoteReport> VoteReportGetAsync()
        {
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT yes_count, no_count, total, yes_percent, no_percent FROM vote_report_get()";

            using var dr = await cmd.ExecuteReaderAsync();
            var report = new VoteReport();

            if (await dr.ReadAsync())
            {
                report.Yes = dr["yes_count"] != DBNull.Value ? Convert.ToInt32(dr["yes_count"]) : 0;
                report.No = dr["no_count"] != DBNull.Value ? Convert.ToInt32(dr["no_count"]) : 0;
                report.Total = dr["total"] != DBNull.Value ? Convert.ToInt32(dr["total"]) : 0;
                report.YesPercent = dr["yes_percent"] != DBNull.Value ? Convert.ToDecimal(dr["yes_percent"]) : 0;
                report.NoPercent = dr["no_percent"] != DBNull.Value ? Convert.ToDecimal(dr["no_percent"]) : 0;
            }

            return report;
        }

        /// <summary>
        /// Opens and immediately closes a connection to Azure Database
        /// for PostgreSQL, without executing any query. The exception is
        /// intentionally not caught here; it is passed to the caller so
        /// the application's startup logic can print a clear error
        /// message.
        /// </summary>
        public async Task TestConnectionAsync()
        {
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
        }

        public async Task EnsureSchemaAsync()
        {
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            using (var checkCmd = connection.CreateCommand())
            {
                checkCmd.CommandText = "SELECT to_regclass('public.vote')";
                if (await checkCmd.ExecuteScalarAsync() is not DBNull and not null)
                {
                    return;
                }
            }

            foreach (var batch in SchemaScripts.PostgreSql)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = batch;
                await cmd.ExecuteNonQueryAsync();
            }
        }
    }
}
