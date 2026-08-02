using ScaleTrigger.Models;

namespace ScaleTrigger
{
    /// <summary>
    /// Reads LoadConfig's seed values from appsettings.json's "Load"
    /// section. Shared by Program.cs (startup) and VoteApiController.Reset() -
    /// keep in sync with LoadConfigApiController.KnownSettingNames.
    /// </summary>
    public static class LoadConfigDefaults
    {
        private static readonly string[] SettingNames =
        {
            "CpuIterationsPerVote",
            "MemoryKilobytesPerVote",
            "DiskWriteKilobytesPerVote",
            "NetworkLatencyMillisecondsPerVote",
            "PayloadBytesPerVote",
            "DbCpuIterationsPerVote",
            "ConfigRefresh"
        };

        public static List<LoadConfigSetting> ReadFrom(IConfiguration configuration)
        {
            var defaults = new List<LoadConfigSetting>();

            foreach (var settingName in SettingNames)
            {
                if (!int.TryParse(configuration[$"Load:{settingName}:Min"], out int min))
                {
                    min = 0;
                }

                if (!int.TryParse(configuration[$"Load:{settingName}:Max"], out int max))
                {
                    max = 0;
                }

                defaults.Add(new LoadConfigSetting { SettingName = settingName, Min = min, Max = max });
            }

            return defaults;
        }
    }
}
