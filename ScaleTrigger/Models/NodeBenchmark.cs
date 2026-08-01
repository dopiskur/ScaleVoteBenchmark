namespace ScaleTrigger.Models
{
    public class NodeHardwareInfo
    {
        public string Environment { get; set; } = string.Empty;
        public string Cpu { get; set; } = string.Empty;
        public int ProcessorCount { get; set; }
        public long TotalMemoryMb { get; set; }
        public double DiskTotalGb { get; set; }
    }

    public class NodeBenchmarkResult
    {
        public NodeHardwareInfo Hardware { get; set; } = new();
        public double CpuNumbersPerSecond { get; set; }
        public double MemoryMbPerSecond { get; set; }
        public double DiskMbPerSecond { get; set; }
    }
}
