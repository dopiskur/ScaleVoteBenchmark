using ScaleTrigger.Models;

namespace ScaleTrigger
{
    /// <summary>
    /// Reads the seed values for every LoadConfig setting from
    /// appsettings.json's "Load" section. Used both at startup (via
    /// LoadConfigEnsureSeededAsync, a no-op if the table already has
    /// rows) and by VoteApiController.Reset(), which drops and recreates
    /// the schema from scratch - the same list of names as
    /// LoadConfigApiController.KnownSettingNames.
    /// </summary>
    public static class LoadConfigDefaults
    {
        private static readonly string[] SettingNames =
        {
            "CpuIterationsPerVote",
            "MemoryMegabytesPerVote",
            "DiskWriteKilobytesPerVote",
            "NetworkLatencyMillisecondsPerVote",
            "PayloadBytesPerVote",
            "DbHashIterationsPerVote",
            "ConfigRefresh"
        };

        public static List<LoadConfigSetting> ReadFrom(IConfiguration configuration)
        {
            var defaults = new List<LoadConfigSetting>();

            foreach (var settingName in SettingNames)
            {
                int min = int.Parse(configuration[$"Load:{settingName}:Min"] ?? "0");
                int max = int.Parse(configuration[$"Load:{settingName}:Max"] ?? "0");
                defaults.Add(new LoadConfigSetting { SettingName = settingName, Min = min, Max = max });
            }

            return defaults;
        }
    }
}
