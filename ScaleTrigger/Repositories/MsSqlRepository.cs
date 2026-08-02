using System.Data;
using Microsoft.Data.SqlClient;
using ScaleTrigger.Azure;
using ScaleTrigger.Interfaces;
using ScaleTrigger.Models;
using ScaleTrigger.Schema;

namespace ScaleTrigger.Repositories
{
    public class MsSqlRepository : IRepository
    {
        private readonly string connectionString;
        private readonly bool useManagedIdentity;

        public MsSqlRepository(string connectionString, bool useManagedIdentity = false)
        {
            this.connectionString = connectionString;
            this.useManagedIdentity = useManagedIdentity;
        }

        private async Task<SqlConnection> CreateConnectionAsync()
        {
            var connection = new SqlConnection(connectionString);

            // Default (false) uses the connection string's plain-text
            // password - simpler for local dev, but less secure than
            // Managed Identity, which is recommended in Azure.
            if (useManagedIdentity)
            {
                connection.AccessToken = await AzureSqlAuthProvider.GetAccessTokenAsync();
            }

            await connection.OpenAsync();
            return connection;
        }

        public async Task VoteAddAsync(string option, byte[]? payload, int maxPrime)
        {
            using var connection = await CreateConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "VoteAdd";
            cmd.CommandType = System.Data.CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@Option", option);

            // AddWithValue can't infer a type from a bare DBNull.Value.
            cmd.Parameters.Add(new SqlParameter("@Payload", SqlDbType.VarBinary, -1)
            {
                Value = (object?)payload ?? DBNull.Value
            });

            cmd.Parameters.AddWithValue("@MaxPrime", maxPrime);

            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>Calls DbCpuBurn directly - chained SHA-512 CPU burn, no INSERT into Vote/Payload at all.</summary>
        public async Task DbCpuBurnAsync(int maxPrime)
        {
            using var connection = await CreateConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DbCpuBurn";
            cmd.CommandType = System.Data.CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@Iterations", maxPrime);
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// dbo.VoteReportGet reads with WITH (NOLOCK), so this doesn't wait
        /// behind a concurrent VoteAdd insert's lock under SQL Server's
        /// default READ COMMITTED (locking, not snapshot) isolation.
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
                checkCmd.CommandText = "SELECT OBJECT_ID('dbo.Vote', 'U')";
                if (await checkCmd.ExecuteScalarAsync() is not DBNull and not null)
                {
                    return;
                }
            }

            foreach (var batch in SchemaScripts.MsSql)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = batch;
                await cmd.ExecuteNonQueryAsync();
            }
        }

        public async Task DropSchemaAsync()
        {
            using var connection = await CreateConnectionAsync();

            // Best-effort: force out every other connection and roll back
            // their transactions immediately - not waiting on whatever lock
            // they're holding - before dropping anything, so a reset can't
            // hang behind someone else's open transaction. Needs ALTER
            // DATABASE rights; silently skipped if the configured account
            // doesn't have them (e.g. a restricted shared/managed
            // database), in which case the DROPs below just run normally
            // and may themselves block on any existing lock.
            bool forcedSingleUser = false;
            try
            {
                using var singleUserCmd = connection.CreateCommand();
                singleUserCmd.CommandText = $"ALTER DATABASE [{connection.Database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;";
                await singleUserCmd.ExecuteNonQueryAsync();
                forcedSingleUser = true;

                // ROLLBACK IMMEDIATE just killed every other session against this
                // database server-side, including whatever idle connections this
                // process's own pool was holding for reuse - the pool has no way
                // to know that. Without clearing it, the next CreateConnectionAsync()
                // anywhere in the app (even in a different request) can be handed
                // one of those now-dead connections and hang or fail on it.
                SqlConnection.ClearPool(connection);
            }
            catch
            {
                // No ALTER DATABASE permission - proceed without forcing other connections out.
            }

            try
            {
                foreach (var batch in SchemaScripts.MsSqlDrop)
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = batch;
                    await cmd.ExecuteNonQueryAsync();
                }

                // Best-effort: reclaims data + log file space; may fail on a
                // shared/managed instance without sufficient permission.
                try
                {
                    using var shrinkCmd = connection.CreateCommand();
                    shrinkCmd.CommandText = "DBCC SHRINKDATABASE (0);";
                    await shrinkCmd.ExecuteNonQueryAsync();
                }
                catch
                {
                    // Shrinking is a nice-to-have.
                }
            }
            finally
            {
                // Always undo SINGLE_USER if we set it, even if the DROPs
                // above failed, so the database isn't left refusing every
                // other connection.
                if (forcedSingleUser)
                {
                    try
                    {
                        using var multiUserCmd = connection.CreateCommand();
                        multiUserCmd.CommandText = $"ALTER DATABASE [{connection.Database}] SET MULTI_USER;";
                        await multiUserCmd.ExecuteNonQueryAsync();
                    }
                    catch
                    {
                        // Best-effort restore.
                    }
                }
            }
        }

        public async Task LoadConfigEnsureSeededAsync(IEnumerable<LoadConfigSetting> defaults)
        {
            using var connection = await CreateConnectionAsync();

            using (var checkCmd = connection.CreateCommand())
            {
                checkCmd.CommandText = "SELECT OBJECT_ID('dbo.LoadConfig', 'U')";
                if (await checkCmd.ExecuteScalarAsync() is DBNull or null)
                {
                    foreach (var batch in SchemaScripts.MsSqlLoadConfig)
                    {
                        using var createCmd = connection.CreateCommand();
                        createCmd.CommandText = batch;
                        await createCmd.ExecuteNonQueryAsync();
                    }
                }
            }

            using (var countCmd = connection.CreateCommand())
            {
                countCmd.CommandText = "SELECT COUNT(*) FROM dbo.LoadConfig";
                int count = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
                if (count > 0)
                {
                    return;
                }
            }

            foreach (var setting in defaults)
            {
                using var insertCmd = connection.CreateCommand();
                insertCmd.CommandText = "INSERT INTO dbo.LoadConfig (SettingName, MinValue, MaxValue) VALUES (@Name, @Min, @Max)";
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
            using var transaction = connection.BeginTransaction();

            foreach (var setting in settings)
            {
                using var cmd = connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = "LoadConfigSet";
                cmd.CommandType = System.Data.CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@SettingName", setting.SettingName);
                cmd.Parameters.AddWithValue("@MinValue", setting.Min);
                cmd.Parameters.AddWithValue("@MaxValue", setting.Max);
                await cmd.ExecuteNonQueryAsync();
            }

            transaction.Commit();
        }
    }
}
