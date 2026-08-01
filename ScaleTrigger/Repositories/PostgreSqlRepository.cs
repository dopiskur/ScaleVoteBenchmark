using Npgsql;
using NpgsqlTypes;
using ScaleTrigger.Interfaces;
using ScaleTrigger.Models;
using ScaleTrigger.Schema;

namespace ScaleTrigger.Repositories
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

        public async Task VoteAddAsync(string option, byte[]? payload)
        {
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "CALL vote_add(@option, @payload)";
            cmd.Parameters.AddWithValue("option", option);

            // AddWithValue can't infer a Postgres type from a bare
            // DBNull.Value, so the parameter type is set explicitly.
            cmd.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Bytea)
            {
                Value = (object?)payload ?? DBNull.Value
            });

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<VoteReport> VoteReportGetAsync()
        {
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText =
                "SELECT yes_count, no_count, total, yes_percent, no_percent, payload_count, payload_total_bytes " +
                "FROM vote_report_get()";

            using var dr = await cmd.ExecuteReaderAsync();
            var report = new VoteReport();

            if (await dr.ReadAsync())
            {
                report.Yes = dr["yes_count"] != DBNull.Value ? Convert.ToInt32(dr["yes_count"]) : 0;
                report.No = dr["no_count"] != DBNull.Value ? Convert.ToInt32(dr["no_count"]) : 0;
                report.Total = dr["total"] != DBNull.Value ? Convert.ToInt32(dr["total"]) : 0;
                report.YesPercent = dr["yes_percent"] != DBNull.Value ? Convert.ToDecimal(dr["yes_percent"]) : 0;
                report.NoPercent = dr["no_percent"] != DBNull.Value ? Convert.ToDecimal(dr["no_percent"]) : 0;
                report.PayloadCount = dr["payload_count"] != DBNull.Value ? Convert.ToInt32(dr["payload_count"]) : 0;
                report.PayloadTotalBytes = dr["payload_total_bytes"] != DBNull.Value ? Convert.ToInt64(dr["payload_total_bytes"]) : 0;
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

        public async Task CleanupAsync()
        {
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "CALL database_cleanup()";
                await cmd.ExecuteNonQueryAsync();
            }

            // Best-effort: VACUUM FULL reclaims the space TRUNCATE freed.
            // Not fatal if this fails (e.g. insufficient permission on a
            // shared/managed instance) - the destructive part already
            // succeeded above. VACUUM cannot run inside a transaction
            // block, so each table is vacuumed as its own standalone
            // command rather than from within a stored procedure.
            try
            {
                using var vacuumVoteCmd = connection.CreateCommand();
                vacuumVoteCmd.CommandText = "VACUUM FULL vote;";
                await vacuumVoteCmd.ExecuteNonQueryAsync();

                using var vacuumPayloadCmd = connection.CreateCommand();
                vacuumPayloadCmd.CommandText = "VACUUM FULL payload;";
                await vacuumPayloadCmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // Ignored - vacuuming is a nice-to-have, not required for
                // the cleanup itself to be considered successful.
            }
        }
    }
}
