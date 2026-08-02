namespace ScaleTrigger.Models
{
    /// <summary>Fully computed by the database - matches exactly what the dashboard displays.</summary>
    public class VoteReport
    {
        public long Total { get; set; }
        public long PayloadCount { get; set; }
        public long PayloadTotalBytes { get; set; }
    }

    /// <summary>One row of the LoadConfig table.</summary>
    public class LoadConfigSetting
    {
        public string SettingName { get; set; } = string.Empty;
        public int Min { get; set; }
        public int Max { get; set; }
    }
}
