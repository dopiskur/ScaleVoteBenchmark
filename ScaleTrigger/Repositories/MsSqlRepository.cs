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

        public async Task<VoteReport> VoteReportGetAsync()
        {
            using var connection = await CreateConnectionAsync();
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
