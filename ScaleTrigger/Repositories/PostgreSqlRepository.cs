using Npgsql;
using NpgsqlTypes;
using ScaleTrigger.Interfaces;
using ScaleTrigger.Models;
using ScaleTrigger.Schema;

namespace ScaleTrigger.Repositories
{
    public class PostgreSqlRepository : IRepository
    {
        private readonly string connectionString;

        public PostgreSqlRepository(string connectionString)
        {
            this.connectionString = connectionString;
        }

        private async Task<NpgsqlConnection> CreateConnectionAsync()
        {
            var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            return connection;
        }

        public async Task VoteAddAsync(string option, byte[]? payload, int maxPrime)
        {
            using var connection = await CreateConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "CALL vote_add(@option, @payload, @maxPrime)";
            cmd.Parameters.AddWithValue("option", option);

            // AddWithValue can't infer a type from a bare DBNull.Value.
            cmd.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Bytea)
            {
                Value = (object?)payload ?? DBNull.Value
            });

            cmd.Parameters.AddWithValue("maxPrime", maxPrime);

            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>Calls db_cpu_burn directly - set-based CPU burn, no INSERT into vote/payload at all.</summary>
        public async Task DbCpuBurnAsync(int maxPrime)
        {
            using var connection = await CreateConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "CALL db_cpu_burn(@maxPrime)";
            cmd.Parameters.AddWithValue("maxPrime", maxPrime);
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Postgres has no NOLOCK/READ UNCOMMITTED hint, but none is needed:
        /// a plain SELECT is always an MVCC snapshot read here, so it never
        /// waits on a concurrent vote_add insert's row lock. Columns are
        /// aliased to PascalCase here so RepositoryMappers.MapVoteReport can
        /// use the exact same column names as the other three providers.
        /// </summary>
        public async Task<VoteReport> VoteReportGetAsync()
        {
            using var connection = await CreateConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText =
                "SELECT total AS \"Total\", payload_count AS \"PayloadCount\", payload_total_bytes AS \"PayloadTotalBytes\" " +
                "FROM vote_report_get()";

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

        public async Task DropSchemaAsync()
        {
            using var connection = await CreateConnectionAsync();

            // Best-effort: terminate every other backend connected to this
            // database - which rolls back any transaction it's holding
            // immediately, not waiting on whatever lock it's holding -
            // before dropping anything, so a reset can't hang behind
            // someone else's transaction. Needs superuser or the
            // pg_signal_backend role; silently skipped if the configured
            // role doesn't have it, in which case the DROPs below just run
            // normally and may themselves block on any existing lock.
            try
            {
                using var terminateCmd = connection.CreateCommand();
                terminateCmd.CommandText =
                    "SELECT pg_terminate_backend(pid) FROM pg_stat_activity " +
                    "WHERE datname = current_database() AND pid != pg_backend_pid();";
                await terminateCmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // Insufficient privilege to terminate other backends - proceed without forcing them out.
            }

            // No separate shrink step: DROP TABLE frees the underlying
            // file(s) immediately, so there's nothing left for VACUUM.
            foreach (var batch in SchemaScripts.PostgreSqlDrop)
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
                checkCmd.CommandText = "SELECT to_regclass('public.load_config')";
                if (await checkCmd.ExecuteScalarAsync() is DBNull or null)
                {
                    foreach (var batch in SchemaScripts.PostgreSqlLoadConfig)
                    {
                        using var createCmd = connection.CreateCommand();
                        createCmd.CommandText = batch;
                        await createCmd.ExecuteNonQueryAsync();
                    }
                }
            }

            using (var countCmd = connection.CreateCommand())
            {
                countCmd.CommandText = "SELECT COUNT(*) FROM load_config";
                long count = (long)(await countCmd.ExecuteScalarAsync())!;
                if (count > 0)
                {
                    return;
                }
            }

            foreach (var setting in defaults)
            {
                using var insertCmd = connection.CreateCommand();
                insertCmd.CommandText = "INSERT INTO load_config (setting_name, min_value, max_value) VALUES (@name, @min, @max)";
                insertCmd.Parameters.AddWithValue("name", setting.SettingName);
                insertCmd.Parameters.AddWithValue("min", setting.Min);
                insertCmd.Parameters.AddWithValue("max", setting.Max);
                await insertCmd.ExecuteNonQueryAsync();
            }
        }

        /// <summary>Columns aliased to PascalCase so RepositoryMappers.MapLoadConfigSetting can use the exact same column names as the other three providers.</summary>
        public async Task<List<LoadConfigSetting>> LoadConfigGetAsync()
        {
            using var connection = await CreateConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText =
                "SELECT setting_name AS \"SettingName\", min_value AS \"MinValue\", max_value AS \"MaxValue\" " +
                "FROM load_config_get()";

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
                cmd.CommandText = "CALL load_config_set(@name, @min, @max)";
                cmd.Parameters.AddWithValue("name", setting.SettingName);
                cmd.Parameters.AddWithValue("min", setting.Min);
                cmd.Parameters.AddWithValue("max", setting.Max);
                await cmd.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
    }
}
