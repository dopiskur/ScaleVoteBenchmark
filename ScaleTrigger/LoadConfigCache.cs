using ScaleTrigger.Models;

namespace ScaleTrigger
{
    /// <summary>In-memory snapshot of the LoadConfig table, kept fresh by LoadConfigRefreshService.</summary>
    public class LoadConfigCache
    {
        private volatile Dictionary<string, (int Min, int Max)> current = new();

        public void Set(IEnumerable<LoadConfigSetting> settings)
        {
            current = settings.ToDictionary(s => s.SettingName, s => (s.Min, s.Max));
        }

        /// <summary>Returns (0, 0) for an unknown setting name.</summary>
        public (int Min, int Max) Get(string settingName)
        {
            return current.TryGetValue(settingName, out var range) ? range : (0, 0);
        }
    }
}
