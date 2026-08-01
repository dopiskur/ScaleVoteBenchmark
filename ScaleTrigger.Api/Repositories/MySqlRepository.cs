using MySqlConnector;
using ScaleTrigger.Api.Interfaces;
using ScaleTrigger.Api.Models;
using ScaleTrigger.Api.Schema;

namespace ScaleTrigger.Api.Repositories
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

        public async Task VoteAddAsync(string option, byte[]? payload)
        {
            using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "VoteAdd";
            cmd.CommandType = System.Data.CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("pOption", option);

            // AddWithValue can't infer a MySQL type from a bare
            // DBNull.Value, so the parameter type is set explicitly.
            cmd.Parameters.Add(new MySqlParameter("pPayload", MySqlDbType.LongBlob)
            {
                Value = (object?)payload ?? DBNull.Value
            });

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<VoteReport> VoteReportGetAsync()
        {
            using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "VoteReportGet";
            cmd.CommandType = System.Data.CommandType.StoredProcedure;

            using var dr = await cmd.ExecuteReaderAsync();
            var report = new VoteReport();

            if (await dr.ReadAsync())
            {
                report.Yes = dr["Yes"] != DBNull.Value ? Convert.ToInt32(dr["Yes"]) : 0;
                report.No = dr["No"] != DBNull.Value ? Convert.ToInt32(dr["No"]) : 0;
                report.Total = dr["Total"] != DBNull.Value ? Convert.ToInt32(dr["Total"]) : 0;
                report.YesPercent = dr["YesPercent"] != DBNull.Value ? Convert.ToDecimal(dr["YesPercent"]) : 0;
                report.NoPercent = dr["NoPercent"] != DBNull.Value ? Convert.ToDecimal(dr["NoPercent"]) : 0;
                report.PayloadCount = dr["PayloadCount"] != DBNull.Value ? Convert.ToInt32(dr["PayloadCount"]) : 0;
                report.PayloadTotalBytes = dr["PayloadTotalBytes"] != DBNull.Value ? Convert.ToInt64(dr["PayloadTotalBytes"]) : 0;
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
        public async Task TestConnectionAsync()
        {
            using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();
        }

        public async Task EnsureSchemaAsync()
        {
            using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();

            using (var checkCmd = connection.CreateCommand())
            {
                checkCmd.CommandText =
                    "SELECT COUNT(*) FROM information_schema.tables " +
                    "WHERE table_schema = DATABASE() AND table_name = 'Vote'";
                var count = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
                if (count > 0)
                {
                    return;
                }
            }

            foreach (var batch in SchemaScripts.MySql)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = batch;
                await cmd.ExecuteNonQueryAsync();
            }
        }

        public async Task CleanupAsync()
        {
            using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "DatabaseCleanup";
                cmd.CommandType = System.Data.CommandType.StoredProcedure;
                await cmd.ExecuteNonQueryAsync();
            }

            // Best-effort: OPTIMIZE TABLE rebuilds the table to reclaim
            // the space TRUNCATE freed. Not fatal if this fails (e.g.
            // insufficient permission on a shared/managed instance) - the
            // destructive part already succeeded above.
            try
            {
                using var optimizeCmd = connection.CreateCommand();
                optimizeCmd.CommandText = "OPTIMIZE TABLE `Payload`, `Vote`;";
                await optimizeCmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // Ignored - optimizing is a nice-to-have, not required for
                // the cleanup itself to be considered successful.
            }
        }
    }
}
