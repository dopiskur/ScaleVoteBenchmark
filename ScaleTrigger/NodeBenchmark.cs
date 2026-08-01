using System.Diagnostics;
using ScaleTrigger.Models;

namespace ScaleTrigger
{
    /// <summary>
    /// Hardware detection and one-off CPU/memory/disk benchmarks for the
    /// machine ScaleTrigger is running on, triggered manually from the
    /// dashboard - unrelated to the per-vote Load:* simulation.
    /// </summary>
    public static class NodeBenchmark
    {
        private static NodeHardwareInfo? cachedHardwareInfo;

        /// <summary>
        /// Cached after the first call, since none of this changes while
        /// the process is running. CPU descriptor comes from whichever
        /// environment we can detect: WEBSITE_SKU on Azure App Service,
        /// the EC2 instance type on AWS (via IMDSv2, which only responds
        /// inside AWS - a short timeout keeps this from stalling
        /// elsewhere), or just the processor count otherwise (generic
        /// VM/Hyper-V/ESXi/KVM/bare metal).
        /// </summary>
        public static async Task<NodeHardwareInfo> GetHardwareInfoAsync()
        {
            if (cachedHardwareInfo != null)
            {
                return cachedHardwareInfo;
            }

            string environment;
            string cpu;

            string? websiteSku = System.Environment.GetEnvironmentVariable("WEBSITE_SKU");
            if (!string.IsNullOrEmpty(websiteSku))
            {
                environment = "Azure";
                cpu = websiteSku;
            }
            else
            {
                string? awsInstanceType = await TryGetAwsInstanceTypeAsync();
                if (!string.IsNullOrEmpty(awsInstanceType))
                {
                    environment = "AWS";
                    cpu = awsInstanceType;
                }
                else
                {
                    environment = "Generic";
                    cpu = $"{System.Environment.ProcessorCount} vCPUs";
                }
            }

            long totalMemoryMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024);

            double diskTotalGb = 0;
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(AppContext.BaseDirectory) ?? Path.GetPathRoot(Environment.CurrentDirectory)!);
                diskTotalGb = Math.Round(drive.TotalSize / (1024.0 * 1024 * 1024), 1);
            }
            catch
            {
                // Best-effort - not every environment exposes drive info.
            }

            cachedHardwareInfo = new NodeHardwareInfo
            {
                Environment = environment,
                Cpu = cpu,
                ProcessorCount = System.Environment.ProcessorCount,
                TotalMemoryMb = totalMemoryMb,
                DiskTotalGb = diskTotalGb
            };

            return cachedHardwareInfo;
        }

        private static async Task<string?> TryGetAwsInstanceTypeAsync()
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };

                using var tokenRequest = new HttpRequestMessage(HttpMethod.Put, "http://169.254.169.254/latest/api/token");
                tokenRequest.Headers.Add("X-aws-ec2-metadata-token-ttl-seconds", "60");
                using var tokenResponse = await http.SendAsync(tokenRequest);
                if (!tokenResponse.IsSuccessStatusCode)
                {
                    return null;
                }
                string token = await tokenResponse.Content.ReadAsStringAsync();

                using var metadataRequest = new HttpRequestMessage(HttpMethod.Get, "http://169.254.169.254/latest/meta-data/instance-type");
                metadataRequest.Headers.Add("X-aws-ec2-metadata-token", token);
                using var metadataResponse = await http.SendAsync(metadataRequest);

                return metadataResponse.IsSuccessStatusCode
                    ? await metadataResponse.Content.ReadAsStringAsync()
                    : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Sysbench's CPU algorithm (see LoadSimulator.SimulateCpuLoad),
        /// run continuously in 1-second ticks for durationSeconds; the
        /// score is the median numbers-checked-per-tick, smoothing out
        /// one-off scheduling/GC noise.
        /// </summary>
        public static double RunCpuBenchmark(int durationSeconds)
        {
            var samples = new List<double>();

            for (int tick = 0; tick < durationSeconds; tick++)
            {
                var sw = Stopwatch.StartNew();
                long n = 2;
                while (sw.ElapsedMilliseconds < 1000)
                {
                    bool isPrime = true;
                    for (long t = 2; t * t <= n; t++)
                    {
                        if (n % t == 0)
                        {
                            isPrime = false;
                            break;
                        }
                    }
                    GC.KeepAlive(isPrime);
                    n++;
                }
                samples.Add(n);
            }

            return Median(samples);
        }

        /// <summary>
        /// Repeatedly overwrites a single blockMegabytes buffer, timing
        /// each fill; the score is blockMegabytes / median fill time.
        /// </summary>
        public static double RunMemoryBenchmark(int blockMegabytes, int repetitions)
        {
            byte[] buffer = new byte[blockMegabytes * 1024 * 1024];
            var seconds = new List<double>();

            for (int i = 0; i < repetitions; i++)
            {
                var sw = Stopwatch.StartNew();
                Random.Shared.NextBytes(buffer);
                seconds.Add(sw.Elapsed.TotalSeconds);
            }

            GC.KeepAlive(buffer);
            return blockMegabytes / Median(seconds);
        }

        /// <summary>
        /// Repeatedly writes a fresh sizeMegabytes file (WriteThrough +
        /// flush, so it's real disk I/O, not just page cache) and deletes
        /// it immediately; the score is sizeMegabytes / median write time.
        /// </summary>
        public static double RunDiskBenchmark(int sizeMegabytes, int repetitions)
        {
            string dir = Path.Combine(Path.GetTempPath(), "ScaleTrigger", "nodebenchmark");
            Directory.CreateDirectory(dir);

            byte[] buffer = new byte[sizeMegabytes * 1024 * 1024];
            Random.Shared.NextBytes(buffer);

            var seconds = new List<double>();
            for (int i = 0; i < repetitions; i++)
            {
                string path = Path.Combine(dir, $"{Guid.NewGuid():N}.tmp");
                var sw = Stopwatch.StartNew();
                try
                {
                    using var stream = new FileStream(
                        path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                        bufferSize: 4096, FileOptions.WriteThrough);
                    stream.Write(buffer, 0, buffer.Length);
                    stream.Flush(flushToDisk: true);
                }
                finally
                {
                    sw.Stop();
                    File.Delete(path);
                }
                seconds.Add(sw.Elapsed.TotalSeconds);
            }

            return sizeMegabytes / Median(seconds);
        }

        private static double Median(List<double> values)
        {
            var sorted = values.OrderBy(v => v).ToList();
            int count = sorted.Count;
            return count % 2 == 1
                ? sorted[count / 2]
                : (sorted[count / 2 - 1] + sorted[count / 2]) / 2.0;
        }
    }
}
