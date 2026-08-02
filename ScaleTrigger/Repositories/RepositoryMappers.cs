using System.Data;
using ScaleTrigger.Models;

namespace ScaleTrigger.Repositories
{
    /// <summary>
    /// Shared DBNull-safe column mapping for all four repositories. Callers
    /// alias their result set's columns to these exact names (PostgreSql's
    /// snake_case columns get an `AS "Total"`-style quoted alias in its own
    /// query text) so one mapper works across every provider.
    /// </summary>
    public static class RepositoryMappers
    {
        public static VoteReport MapVoteReport(IDataRecord dr)
        {
            return new VoteReport
            {
                Total = dr["Total"] != DBNull.Value ? Convert.ToInt64(dr["Total"]) : 0,
                PayloadCount = dr["PayloadCount"] != DBNull.Value ? Convert.ToInt64(dr["PayloadCount"]) : 0,
                PayloadTotalBytes = dr["PayloadTotalBytes"] != DBNull.Value ? Convert.ToInt64(dr["PayloadTotalBytes"]) : 0
            };
        }

        public static LoadConfigSetting MapLoadConfigSetting(IDataRecord dr)
        {
            return new LoadConfigSetting
            {
                SettingName = (string)dr["SettingName"],
                Min = Convert.ToInt32(dr["MinValue"]),
                Max = Convert.ToInt32(dr["MaxValue"])
            };
        }
    }
}
