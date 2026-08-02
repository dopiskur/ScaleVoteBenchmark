using System.Diagnostics;
using System.Text.Json;
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
        /// the process is running. Checked in order from most to least
        /// specific, each cheap/synchronous signal (env vars set by the
        /// hosting platform) before any network call, and orchestrator
        /// signals (Kubernetes, ECS) before the underlying VM's own
        /// metadata service, since AKS/EKS nodes and EC2-backed ECS tasks
        /// would otherwise be misidentified as a bare VM:
        /// - Azure Container Apps: CONTAINER_APP_NAME env var
        /// - Azure Functions / App Service: WEBSITE_SKU env var (the plan/tier name)
        /// - AWS ECS/Fargate: ECS_CONTAINER_METADATA_URI_V4 env var (task metadata endpoint)
        /// - Kubernetes (AKS, EKS, GKE, on-prem, ...): KUBERNETES_SERVICE_HOST env var
        /// - AWS EC2: instance type via IMDSv2 (only responds inside AWS)
        /// - Azure VM: vmSize via Azure's IMDS (only responds inside Azure)
        /// - plain Docker (no orchestrator): DOTNET_RUNNING_IN_CONTAINER env var / /.dockerenv
        /// - Generic: just the processor count (bare metal/Hyper-V/ESXi/KVM/unrecognized)
        /// Metadata-service calls use a short timeout so a non-matching environment doesn't stall this.
        /// </summary>
        public static async Task<NodeHardwareInfo> GetHardwareInfoAsync()
        {
            if (cachedHardwareInfo != null)
            {
                return cachedHardwareInfo;
            }

            string environment;
            string cpu;

            string? containerAppName = System.Environment.GetEnvironmentVariable("CONTAINER_APP_NAME");
            string? websiteSku = System.Environment.GetEnvironmentVariable("WEBSITE_SKU");
            string? ecsMetadataUri = System.Environment.GetEnvironmentVariable("ECS_CONTAINER_METADATA_URI_V4");
            string? kubernetesHost = System.Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST");

            if (!string.IsNullOrEmpty(containerAppName))
            {
                environment = "Azure Container Apps";
                cpu = $"{System.Environment.ProcessorCount} vCPUs";
            }
            else if (!string.IsNullOrEmpty(websiteSku))
            {
                bool isFunctions = !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME"));
                environment = isFunctions ? "Azure Functions" : "Azure App Service";
                cpu = websiteSku;
            }
            else if (!string.IsNullOrEmpty(ecsMetadataUri))
            {
                environment = "AWS ECS/Fargate";
                cpu = await TryGetAwsEcsSizeAsync(ecsMetadataUri) ?? $"{System.Environment.ProcessorCount} vCPUs";
            }
            else if (!string.IsNullOrEmpty(kubernetesHost))
            {
                environment = "Kubernetes";
                cpu = $"{System.Environment.ProcessorCount} vCPUs";
            }
            else
            {
                string? awsInstanceType = await TryGetAwsEc2InstanceTypeAsync();
                if (!string.IsNullOrEmpty(awsInstanceType))
                {
                    environment = "AWS EC2";
                    cpu = awsInstanceType;
                }
                else
                {
                    string? azureVmSize = await TryGetAzureVmSizeAsync();
                    if (!string.IsNullOrEmpty(azureVmSize))
                    {
                        environment = "Azure VM";
                        cpu = azureVmSize;
                    }
                    else if (IsRunningInContainer())
                    {
                        environment = "Container";
                        cpu = $"{System.Environment.ProcessorCount} vCPUs";
                    }
                    else
                    {
                        environment = "Generic";
                        cpu = $"{System.Environment.ProcessorCount} vCPUs";
                    }
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

        /// <summary>Opens an HttpClient with a 500ms timeout and runs fetch against it, swallowing any exception (timeout, DNS, connection refused) into a null result - the shared shape behind every metadata-service probe below, none of which should ever throw out to GetHardwareInfoAsync.</summary>
        private static async Task<string?> TryFetchAsync(Func<HttpClient, Task<string?>> fetch)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
                return await fetch(http);
            }
            catch
            {
                return null;
            }
        }

        private static Task<string?> TryGetAwsEc2InstanceTypeAsync()
        {
            return TryFetchAsync(async http =>
            {
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
            });
        }

        /// <summary>Azure's IMDS (distinct from AWS's) - only reachable from inside an actual Azure VM, not App Service/Container Apps.</summary>
        private static Task<string?> TryGetAzureVmSizeAsync()
        {
            return TryFetchAsync(async http =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get,
                    "http://169.254.169.254/metadata/instance/compute/vmSize?api-version=2021-02-01&format=text");
                request.Headers.Add("Metadata", "true");
                using var response = await http.SendAsync(request);

                return response.IsSuccessStatusCode
                    ? await response.Content.ReadAsStringAsync()
                    : null;
            });
        }

        /// <summary>Reads the task's CPU/memory reservation off the ECS task metadata endpoint (available inside ECS/Fargate tasks only).</summary>
        private static Task<string?> TryGetAwsEcsSizeAsync(string metadataUri)
        {
            return TryFetchAsync(async http =>
            {
                using var response = await http.GetAsync($"{metadataUri}/task");
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
                if (!doc.RootElement.TryGetProperty("Limits", out var limits))
                {
                    return null;
                }

                string? cpuUnits = limits.TryGetProperty("CPU", out var cpuEl) ? cpuEl.ToString() : null;
                string? memoryMb = limits.TryGetProperty("Memory", out var memEl) ? memEl.ToString() : null;

                return cpuUnits != null || memoryMb != null
                    ? $"{cpuUnits ?? "?"} CPU units / {memoryMb ?? "?"} MiB"
                    : null;
            });
        }

        /// <summary>Set by Microsoft's official .NET Docker base images; /.dockerenv is Docker's own marker file on Linux hosts.</summary>
        private static bool IsRunningInContainer()
        {
            return string.Equals(
                    System.Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
                    "true", StringComparison.OrdinalIgnoreCase)
                || File.Exists("/.dockerenv");
        }

        /// <summary>
        /// Sysbench's CPU algorithm (see LoadSimulator.SimulateCpuLoad), run
        /// on every logical processor at once, each in continuous 1-second
        /// ticks for durationSeconds - this is a full-node saturation test
        /// (ScaleTrigger's purpose is verifying autoscale triggers, which
        /// react to total instance CPU%, not one core), not a single-thread
        /// hardware comparison score. Each thread's score is the median
        /// numbers-checked-per-tick, smoothing out one-off scheduling/GC
        /// noise; the overall score is the sum across threads.
        /// </summary>
        public static double RunCpuBenchmark(int durationSeconds)
        {
            int threadCount = Environment.ProcessorCount;
            var perThreadScores = new double[threadCount];

            Parallel.For(0, threadCount, threadIndex =>
            {
                var samples = new List<double>();

                for (int tick = 0; tick < durationSeconds; tick++)
                {
                    var sw = Stopwatch.StartNew();
                    long n = 2;
                    while (sw.ElapsedMilliseconds < 1000)
                    {
                        bool isPrime = LoadSimulator.IsPrime(n);
                        GC.KeepAlive(isPrime);
                        n++;
                    }
                    samples.Add(n);
                }

                perThreadScores[threadIndex] = Median(samples);
            });

            return perThreadScores.Sum();
        }

        /// <summary>
        /// Repeatedly overwrites a single blockMegabytes buffer, timing
        /// each fill; the score is blockMegabytes / median fill time.
        /// </summary>
        public static double RunMemoryBenchmark(int blockMegabytes, int repetitions)
        {
            long bufferSizeBytes = (long)blockMegabytes * 1024 * 1024;
            byte[] buffer = new byte[bufferSizeBytes];
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
        /// environment comes from the GetHardwareInfoAsync call the caller
        /// already made - see LoadSimulator.ResolveDiskLoadDirectoryAsync
        /// for why Kubernetes gets its own working-directory path instead
        /// of the temp-path default.
        /// </summary>
        public static double RunDiskBenchmark(int sizeMegabytes, int repetitions, string environment)
        {
            string dir = environment == "Kubernetes"
                ? Path.Combine(AppContext.BaseDirectory, "nodebenchmark")
                : Path.Combine(Path.GetTempPath(), "ScaleTrigger", "nodebenchmark");
            Directory.CreateDirectory(dir);

            long bufferSizeBytes = (long)sizeMegabytes * 1024 * 1024;
            byte[] buffer = new byte[bufferSizeBytes];
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
