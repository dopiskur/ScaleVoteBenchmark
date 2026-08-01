using ScaleTrigger.Models;

namespace ScaleTrigger
{
    /// <summary>
    /// In-memory snapshot of the LoadConfig table, kept fresh by
    /// LoadConfigRefreshService. VoteApiController reads from this on
    /// every vote instead of querying the database directly, so a vote
    /// never waits on an extra round-trip just to look up its own load
    /// intensity.
    /// </summary>
    public class LoadConfigCache
    {
        private volatile Dictionary<string, (int Min, int Max)> current = new();

        public void Set(IEnumerable<LoadConfigSetting> settings)
        {
            current = settings.ToDictionary(s => s.SettingName, s => (s.Min, s.Max));
        }

        /// <summary>
        /// Returns (0, 0) for a setting name not currently in the cache
        /// (e.g. the very first request racing the initial load).
        /// </summary>
        public (int Min, int Max) Get(string settingName)
        {
            return current.TryGetValue(settingName, out var range) ? range : (0, 0);
        }
    }
}
