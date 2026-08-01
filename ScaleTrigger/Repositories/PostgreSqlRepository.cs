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

        public async Task VoteAddAsync(string option, byte[]? payload, int hashIterations)
        {
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "CALL vote_add(@option, @payload, @hashIterations)";
            cmd.Parameters.AddWithValue("option", option);

            // AddWithValue can't infer a type from a bare DBNull.Value.
            cmd.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Bytea)
            {
                Value = (object?)payload ?? DBNull.Value
            });

            cmd.Parameters.AddWithValue("hashIterations", hashIterations);

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

        /// <summary>Exceptions intentionally propagate so startup logic can log a clear error.</summary>
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

        public async Task DropSchemaAsync()
        {
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

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
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

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

        public async Task<List<LoadConfigSetting>> LoadConfigGetAsync()
        {
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT setting_name, min_value, max_value FROM load_config_get()";

            var settings = new List<LoadConfigSetting>();
            using var dr = await cmd.ExecuteReaderAsync();
            while (await dr.ReadAsync())
            {
                settings.Add(new LoadConfigSetting
                {
                    SettingName = (string)dr["setting_name"],
                    Min = Convert.ToInt32(dr["min_value"]),
                    Max = Convert.ToInt32(dr["max_value"])
                });
            }

            return settings;
        }

        public async Task LoadConfigUpdateAsync(IEnumerable<LoadConfigSetting> settings)
        {
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
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
