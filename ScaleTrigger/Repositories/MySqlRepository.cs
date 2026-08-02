using MySqlConnector;
using ScaleTrigger.Interfaces;
using ScaleTrigger.Models;
using ScaleTrigger.Schema;

namespace ScaleTrigger.Repositories
{
    public class MySqlRepository : IRepository
    {
        private readonly string connectionString;

        public MySqlRepository(string connectionString)
        {
            this.connectionString = connectionString;
        }

        private async Task<MySqlConnection> CreateConnectionAsync()
        {
            var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();
            return connection;
        }

        public async Task VoteAddAsync(string option, byte[]? payload, int maxPrime)
        {
            using var connection = await CreateConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "VoteAdd";
            cmd.CommandType = System.Data.CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("pOption", option);

            // AddWithValue can't infer a type from a bare DBNull.Value.
            cmd.Parameters.Add(new MySqlParameter("pPayload", MySqlDbType.LongBlob)
            {
                Value = (object?)payload ?? DBNull.Value
            });

            cmd.Parameters.AddWithValue("pMaxPrime", maxPrime);

            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>Calls DbCpuBurn directly - set-based CPU burn, no INSERT into Vote/Payload at all.</summary>
        public async Task DbCpuBurnAsync(int maxPrime)
        {
            using var connection = await CreateConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DbCpuBurn";
            cmd.CommandType = System.Data.CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("pMaxPrime", maxPrime);
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// MySQL has no NOLOCK/READ UNCOMMITTED hint, but none is needed:
        /// InnoDB's plain SELECT is a non-locking consistent (MVCC) read, so
        /// it never waits on a concurrent VoteAdd insert's row lock.
        /// </summary>
        public async Task<VoteReport> VoteReportGetAsync()
        {
            using var connection = await CreateConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "VoteReportGet";
            cmd.CommandType = System.Data.CommandType.StoredProcedure;

            using var dr = await cmd.ExecuteReaderAsync();

            return await dr.ReadAsync() ? RepositoryMappers.MapVoteReport(dr) : new VoteReport();
        }

        /// <summary>Exceptions intentionally propagate so startup logic can log a clear error.</summary>
        public async Task TestConnectionAsync()
        {
            using var connection = await CreateConnectionAsync();
        }

        public async Task EnsureSchemaAsync()
        {
            using var connection = await CreateConnectionAsync();

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

        public async Task DropSchemaAsync()
        {
            using var connection = await CreateConnectionAsync();

            // Best-effort: kill every other connection to this database -
            // which rolls back any transaction it's holding immediately,
            // not waiting on whatever lock it's holding - before dropping
            // anything, so a reset can't hang behind someone else's
            // transaction. Needs the PROCESS privilege to see other
            // connections and CONNECTION_ADMIN/SUPER to kill them; silently
            // skipped (per-connection, or entirely if listing them fails)
            // if the configured account doesn't have them, in which case
            // the DROPs below just run normally and may themselves block.
            try
            {
                var connectionIdsToKill = new List<long>();
                using (var listCmd = connection.CreateCommand())
                {
                    listCmd.CommandText =
                        "SELECT id FROM information_schema.processlist " +
                        "WHERE db = DATABASE() AND id != CONNECTION_ID()";
                    using var reader = await listCmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        connectionIdsToKill.Add(reader.GetInt64(0));
                    }
                }

                foreach (var id in connectionIdsToKill)
                {
                    try
                    {
                        using var killCmd = connection.CreateCommand();
                        killCmd.CommandText = $"KILL {id};";
                        await killCmd.ExecuteNonQueryAsync();
                    }
                    catch
                    {
                        // Already closed, or not ours to kill - move on to the next one.
                    }
                }

                if (connectionIdsToKill.Count > 0)
                {
                    // Those KILLs just killed every other session against this
                    // database server-side, including whatever idle connections
                    // this process's own pool was holding for reuse - the pool
                    // has no way to know that. Without clearing it, the next
                    // CreateConnectionAsync() anywhere in the app (even in a
                    // different request) can be handed one of those now-dead
                    // connections and hang or fail on it.
                    await MySqlConnection.ClearPoolAsync(connection);
                }
            }
            catch
            {
                // No PROCESS permission to even list other connections - proceed without forcing them out.
            }

            // No separate shrink step: InnoDB's file-per-table storage
            // frees each table's .ibd file as part of DROP TABLE itself.
            foreach (var batch in SchemaScripts.MySqlDrop)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = batch;
                await cmd.ExecuteNonQueryAsync();
            }
        }

        public async Task LoadConfigEnsureSeededAsync(IEnumerable<LoadConfigSetting> defaults)
        {
            using var connection = await CreateConnectionAsync();

            using (var checkCmd = connection.CreateCommand())
            {
                checkCmd.CommandText =
                    "SELECT COUNT(*) FROM information_schema.tables " +
                    "WHERE table_schema = DATABASE() AND table_name = 'LoadConfig'";
                var count = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
                if (count == 0)
                {
                    foreach (var batch in SchemaScripts.MySqlLoadConfig)
                    {
                        using var createCmd = connection.CreateCommand();
                        createCmd.CommandText = batch;
                        await createCmd.ExecuteNonQueryAsync();
                    }
                }
            }

            using (var countCmd = connection.CreateCommand())
            {
                countCmd.CommandText = "SELECT COUNT(*) FROM `LoadConfig`";
                var rowCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
                if (rowCount > 0)
                {
                    return;
                }
            }

            foreach (var setting in defaults)
            {
                using var insertCmd = connection.CreateCommand();
                insertCmd.CommandText = "INSERT INTO `LoadConfig` (`SettingName`, `MinValue`, `MaxValue`) VALUES (@Name, @Min, @Max)";
                insertCmd.Parameters.AddWithValue("@Name", setting.SettingName);
                insertCmd.Parameters.AddWithValue("@Min", setting.Min);
                insertCmd.Parameters.AddWithValue("@Max", setting.Max);
                await insertCmd.ExecuteNonQueryAsync();
            }
        }

        public async Task<List<LoadConfigSetting>> LoadConfigGetAsync()
        {
            using var connection = await CreateConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "LoadConfigGet";
            cmd.CommandType = System.Data.CommandType.StoredProcedure;

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
            using var transaction = await connection.BeginTransactionAsync();

            foreach (var setting in settings)
            {
                using var cmd = connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = "LoadConfigSet";
                cmd.CommandType = System.Data.CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("pSettingName", setting.SettingName);
                cmd.Parameters.AddWithValue("pMinValue", setting.Min);
                cmd.Parameters.AddWithValue("pMaxValue", setting.Max);
                await cmd.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
    }
}
