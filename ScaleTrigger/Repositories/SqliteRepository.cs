using Microsoft.Data.Sqlite;
using ScaleTrigger.Interfaces;
using ScaleTrigger.Models;
using ScaleTrigger.Schema;

namespace ScaleTrigger.Repositories
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

        /// <summary>
        /// Opens a connection and applies the settings SQLite needs to
        /// cope with concurrent votes: a busy timeout so a connection
        /// that finds the database momentarily locked by another writer
        /// retries instead of immediately throwing "database is locked"
        /// (SQLite Error 5), and WAL journal mode so readers (the
        /// dashboard polling /api/vote/report) don't block writers and
        /// vice versa. WAL mode is persisted in the database file itself,
        /// but is set on every connection anyway since it's a cheap no-op
        /// once already enabled, and covers databases created before
        /// this was added.
        /// </summary>
        private async Task<SqliteConnection> CreateConnectionAsync()
        {
            var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA busy_timeout = 5000;";
                await cmd.ExecuteNonQueryAsync();
            }

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA journal_mode = WAL;";
                await cmd.ExecuteNonQueryAsync();
            }

            return connection;
        }

        public async Task VoteAddAsync(string option, byte[]? payload)
        {
            using var connection = await CreateConnectionAsync();

            long newIdVote;
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO Vote (\"Option\") VALUES ($option) RETURNING IDVote";
                cmd.Parameters.AddWithValue("$option", option);
                newIdVote = (long)(await cmd.ExecuteScalarAsync())!;
            }

            if (payload != null)
            {
                using var payloadCmd = connection.CreateCommand();
                payloadCmd.CommandText = "INSERT INTO Payload (IDVote, Data) VALUES ($idVote, $data)";
                payloadCmd.Parameters.AddWithValue("$idVote", newIdVote);
                payloadCmd.Parameters.AddWithValue("$data", payload);
                await payloadCmd.ExecuteNonQueryAsync();
            }
        }

        public async Task<VoteReport> VoteReportGetAsync()
        {
            using var connection = await CreateConnectionAsync();
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
                "END AS NoPercent, " +
                "(SELECT COUNT(*) FROM Payload) AS PayloadCount, " +
                "(SELECT IFNULL(SUM(LENGTH(Data)), 0) FROM Payload) AS PayloadTotalBytes " +
                "FROM Vote";

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
        /// Opens and immediately closes a connection to the SQLite
        /// database file. SQLite creates the file automatically on first
        /// connection if it does not yet exist, so this effectively
        /// always succeeds as long as the configured path is writable.
        /// </summary>
        public async Task TestConnectionAsync()
        {
            using var connection = await CreateConnectionAsync();
        }

        public async Task EnsureSchemaAsync()
        {
            using var connection = await CreateConnectionAsync();

            using (var checkCmd = connection.CreateCommand())
            {
                checkCmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'Vote'";
                if (await checkCmd.ExecuteScalarAsync() != null)
                {
                    return;
                }
            }

            foreach (var batch in SchemaScripts.Sqlite)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = batch;
                await cmd.ExecuteNonQueryAsync();
            }
        }

        public async Task CleanupAsync()
        {
            using var connection = await CreateConnectionAsync();

            async Task ExecAsync(string sql)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = sql;
                await cmd.ExecuteNonQueryAsync();
            }

            await ExecAsync("DELETE FROM Payload;");
            await ExecAsync("DELETE FROM Vote;");

            // sqlite_sequence (backing AUTOINCREMENT) is only created
            // lazily on the first insert, so it may not exist yet on a
            // database nobody has voted on.
            using (var checkCmd = connection.CreateCommand())
            {
                checkCmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'sqlite_sequence'";
                if (await checkCmd.ExecuteScalarAsync() != null)
                {
                    await ExecAsync("DELETE FROM sqlite_sequence WHERE name IN ('Vote', 'Payload');");
                }
            }

            // Truncate the WAL file back to empty, then VACUUM to shrink
            // the main database file - both are best-effort in the sense
            // that the destructive DELETEs above already succeeded
            // regardless of whether reclaiming disk space also works.
            try
            {
                await ExecAsync("PRAGMA wal_checkpoint(TRUNCATE);");
                await ExecAsync("VACUUM;");
            }
            catch
            {
                // Ignored - shrinking is a nice-to-have, not required for
                // the cleanup itself to be considered successful.
            }
        }
    }
}
