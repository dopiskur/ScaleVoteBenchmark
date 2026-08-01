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
            connection.CreateFunction("sysbench_cpu", (long max) =>
            {
                long primeCount = 0;
                for (long n = 2; n <= max; n++)
                {
                    bool isPrime = true;
                    for (long t = 2; t * t <= n; t++)
                    {
                        if (n % t == 0)
                        {
                            isPrime = false;
                            break;
                        }
                    }

                    if (isPrime)
                    {
                        primeCount++;
                    }
                }

                return primeCount;
            });

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT sysbench_cpu($maxPrime);";
            cmd.Parameters.AddWithValue("$maxPrime", maxPrime);
            await cmd.ExecuteScalarAsync();
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
                report.Yes = dr["Yes"] != DBNull.Value ? Convert.ToInt64(dr["Yes"]) : 0;
                report.No = dr["No"] != DBNull.Value ? Convert.ToInt64(dr["No"]) : 0;
                report.Total = dr["Total"] != DBNull.Value ? Convert.ToInt64(dr["Total"]) : 0;
                report.YesPercent = dr["YesPercent"] != DBNull.Value ? Convert.ToDecimal(dr["YesPercent"]) : 0;
                report.NoPercent = dr["NoPercent"] != DBNull.Value ? Convert.ToDecimal(dr["NoPercent"]) : 0;
                report.PayloadCount = dr["PayloadCount"] != DBNull.Value ? Convert.ToInt64(dr["PayloadCount"]) : 0;
                report.PayloadTotalBytes = dr["PayloadTotalBytes"] != DBNull.Value ? Convert.ToInt64(dr["PayloadTotalBytes"]) : 0;
            }

            return report;
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
