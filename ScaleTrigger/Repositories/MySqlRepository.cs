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

        public async Task VoteAddAsync(string option, byte[]? payload, int maxPrime)
        {
            using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();
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

        /// <summary>Exceptions intentionally propagate so startup logic can log a clear error.</summary>
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

        public async Task DropSchemaAsync()
        {
            using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();

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
            using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();

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
            using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();
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
            using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();
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
