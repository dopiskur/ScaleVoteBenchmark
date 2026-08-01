using System.Security.Cryptography;
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

        /// <summary>
        /// Simulates CPU load inside the database engine itself (as
        /// opposed to CpuIterationsPerVote, which runs in the application
        /// before VoteAddAsync is even called). SQLite has neither stored
        /// procedures nor a built-in hash function, so this registers a
        /// SHA-256 scalar function on the connection and chains it
        /// "iterations" times via a recursive CTE - the closest
        /// equivalent available to a hashing loop inside a stored
        /// procedure. The actual hashing still runs as .NET code, but
        /// dispatched from and driven by the SQL engine, matching how the
        /// other three providers do it inside VoteAdd.
        /// </summary>
        private static async Task RunHashLoopAsync(SqliteConnection connection, int iterations)
        {
            connection.CreateFunction("sha2_256", (byte[] data) => SHA256.HashData(data));

            using var cmd = connection.CreateCommand();
            cmd.CommandText =
                "WITH RECURSIVE hash_chain(i, h) AS (" +
                "  SELECT 1, sha2_256(randomblob(32)) " +
                "  UNION ALL " +
                "  SELECT i + 1, sha2_256(h) FROM hash_chain WHERE i < $iterations" +
                ") SELECT h FROM hash_chain ORDER BY i DESC LIMIT 1;";
            cmd.Parameters.AddWithValue("$iterations", iterations);
            await cmd.ExecuteScalarAsync();
        }

        public async Task VoteAddAsync(string option, byte[]? payload, int hashIterations)
        {
            using var connection = await CreateConnectionAsync();

            if (hashIterations > 0)
            {
                await RunHashLoopAsync(connection, hashIterations);
            }

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

        public async Task DropSchemaAsync()
        {
            using var connection = await CreateConnectionAsync();

            foreach (var batch in SchemaScripts.SqliteDrop)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = batch;
                await cmd.ExecuteNonQueryAsync();
            }

            // Truncate the WAL file back to empty, then VACUUM to shrink
            // the main database file - both are best-effort in the sense
            // that the destructive DROPs above already succeeded
            // regardless of whether reclaiming disk space also works.
            try
            {
                using var checkpointCmd = connection.CreateCommand();
                checkpointCmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                await checkpointCmd.ExecuteNonQueryAsync();

                using var vacuumCmd = connection.CreateCommand();
                vacuumCmd.CommandText = "VACUUM;";
                await vacuumCmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // Ignored - shrinking is a nice-to-have, not required for
                // the reset itself to be considered successful.
            }
        }

        public async Task LoadConfigEnsureSeededAsync(IEnumerable<LoadConfigSetting> defaults)
        {
            using var connection = await CreateConnectionAsync();

            using (var checkCmd = connection.CreateCommand())
            {
                checkCmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'LoadConfig'";
                if (await checkCmd.ExecuteScalarAsync() == null)
                {
                    foreach (var batch in SchemaScripts.SqliteLoadConfig)
                    {
                        using var createCmd = connection.CreateCommand();
                        createCmd.CommandText = batch;
                        await createCmd.ExecuteNonQueryAsync();
                    }
                }
            }

            using (var countCmd = connection.CreateCommand())
            {
                countCmd.CommandText = "SELECT COUNT(*) FROM LoadConfig";
                long count = (long)(await countCmd.ExecuteScalarAsync())!;
                if (count > 0)
                {
                    return;
                }
            }

            foreach (var setting in defaults)
            {
                using var insertCmd = connection.CreateCommand();
                insertCmd.CommandText = "INSERT INTO LoadConfig (SettingName, MinValue, MaxValue) VALUES ($name, $min, $max)";
                insertCmd.Parameters.AddWithValue("$name", setting.SettingName);
                insertCmd.Parameters.AddWithValue("$min", setting.Min);
                insertCmd.Parameters.AddWithValue("$max", setting.Max);
                await insertCmd.ExecuteNonQueryAsync();
            }
        }

        public async Task<List<LoadConfigSetting>> LoadConfigGetAsync()
        {
            using var connection = await CreateConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT SettingName, MinValue, MaxValue FROM LoadConfig ORDER BY SettingName";

            var settings = new List<LoadConfigSetting>();
            using var dr = await cmd.ExecuteReaderAsync();
            while (await dr.ReadAsync())
            {
                settings.Add(new LoadConfigSetting
                {
                    SettingName = (string)dr["SettingName"],
                    Min = Convert.ToInt32(dr["MinValue"]),
                    Max = Convert.ToInt32(dr["MaxValue"])
                });
            }

            return settings;
        }

        public async Task LoadConfigUpdateAsync(IEnumerable<LoadConfigSetting> settings)
        {
            using var connection = await CreateConnectionAsync();
            using var transaction = connection.BeginTransaction();

            foreach (var setting in settings)
            {
                using var cmd = connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = "UPDATE LoadConfig SET MinValue = $min, MaxValue = $max, DateUpdated = CURRENT_TIMESTAMP WHERE SettingName = $name";
                cmd.Parameters.AddWithValue("$name", setting.SettingName);
                cmd.Parameters.AddWithValue("$min", setting.Min);
                cmd.Parameters.AddWithValue("$max", setting.Max);
                await cmd.ExecuteNonQueryAsync();
            }

            transaction.Commit();
        }
    }
}
