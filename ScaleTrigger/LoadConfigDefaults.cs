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
            "MemoryMegabytesPerVote",
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
                int min = int.Parse(configuration[$"Load:{settingName}:Min"] ?? "0");
                int max = int.Parse(configuration[$"Load:{settingName}:Max"] ?? "0");
                defaults.Add(new LoadConfigSetting { SettingName = settingName, Min = min, Max = max });
            }

            return defaults;
        }
    }
}
