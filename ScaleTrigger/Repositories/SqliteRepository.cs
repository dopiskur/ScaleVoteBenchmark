using Microsoft.Data.Sqlite;
using ScaleTrigger.Interfaces;
using ScaleTrigger.Models;
using ScaleTrigger.Schema;

namespace ScaleTrigger.Repositories
{
    /// <summary>
    /// SQLite has no stored procedure/function support, so unlike the
    /// other three repositories this one runs plain parameterized SQL
    /// directly instead of calling stored routines.
    /// </summary>
    public class SqliteRepository : IRepository
    {
        private readonly string connectionString;

        public SqliteRepository(string connectionString)
        {
            this.connectionString = connectionString;
        }

        /// <summary>
        /// busy_timeout avoids "database is locked" (Error 5) under
        /// concurrent writers; WAL mode lets readers and writers avoid
        /// blocking each other. Set on every connection since re-applying
        /// is a cheap no-op and covers databases created before this was
        /// added.
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
        /// SQLite has no stored procedures, so this registers sysbench's
        /// CPU algorithm (counts primes up to maxPrime by trial division)
        /// as a scalar function and calls it from SQL - dispatched by
        /// the SQL engine, matching the other three providers' VoteAdd.
        /// </summary>
        private static async Task RunSysbenchCpuAsync(SqliteConnection connection, int maxPrime)
        {
            connection.CreateFunction("sysbench_cpu", (long max) => LoadSimulator.CountPrimesUpTo(max));

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT sysbench_cpu($maxPrime);";
            cmd.Parameters.AddWithValue("$maxPrime", maxPrime);
            await cmd.ExecuteScalarAsync();
        }

        /// <summary>Reuses the same sysbench_cpu scalar function VoteAdd calls, but does no INSERT at all.</summary>
        public async Task DbCpuBurnAsync(int maxPrime)
        {
            using var connection = await CreateConnectionAsync();

            if (maxPrime > 0)
            {
                await RunSysbenchCpuAsync(connection, maxPrime);
            }
        }

        public async Task VoteAddAsync(string option, byte[]? payload, int maxPrime)
        {
            using var connection = await CreateConnectionAsync();

            if (maxPrime > 0)
            {
                await RunSysbenchCpuAsync(connection, maxPrime);
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

        /// <summary>
        /// SQLite has no NOLOCK/READ UNCOMMITTED hint, but none is needed:
        /// WAL mode (see CreateConnectionAsync) already lets this read run
        /// against a snapshot without waiting on a concurrent VoteAdd writer.
        /// </summary>
        public async Task<VoteReport> VoteReportGetAsync()
        {
            using var connection = await CreateConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText =
                "SELECT " +
                "COUNT(*) AS Total, " +
                "(SELECT COUNT(*) FROM Payload) AS PayloadCount, " +
                "(SELECT IFNULL(SUM(LENGTH(Data)), 0) FROM Payload) AS PayloadTotalBytes " +
                "FROM Vote";

            using var dr = await cmd.ExecuteReaderAsync();

            return await dr.ReadAsync() ? RepositoryMappers.MapVoteReport(dr) : new VoteReport();
        }

        /// <summary>SQLite creates the file on first connection, so this succeeds as long as the path is writable.</summary>
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

            // SQLite has no server-side connections to kill - a lock here
            // is another process/handle holding the same file, which
            // nothing in this process can forcibly evict. The closest
            // equivalent to "don't wait, ignore the lock" is to stop
            // CreateConnectionAsync's normal 5s busy_timeout from waiting
            // at all: fail fast with SQLITE_BUSY instead of blocking, so a
            // reset can't hang behind someone else's write lock.
            using (var busyTimeoutCmd = connection.CreateCommand())
            {
                busyTimeoutCmd.CommandText = "PRAGMA busy_timeout = 0;";
                await busyTimeoutCmd.ExecuteNonQueryAsync();
            }

            foreach (var batch in SchemaScripts.SqliteDrop)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = batch;
                await cmd.ExecuteNonQueryAsync();
            }

            // Best-effort: the DROPs above already succeeded regardless.
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
                // Shrinking is a nice-to-have.
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
                settings.Add(RepositoryMappers.MapLoadConfigSetting(dr));
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
