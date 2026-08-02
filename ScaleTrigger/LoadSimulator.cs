namespace ScaleTrigger
{
    public static class LoadSimulator
    {
        private static readonly Lazy<Task<string>> DiskLoadDirectory = new(ResolveDiskLoadDirectoryAsync);

        /// <summary>
        /// Kubernetes commonly mounts /tmp as an emptyDir with medium: Memory
        /// (especially alongside readOnlyRootFilesystem: true), making
        /// Path.GetTempPath() tmpfs rather than real disk there - so disk
        /// load on Kubernetes instead targets the app's own working
        /// directory, which normally sits on the container's writable layer.
        /// Every other detected environment (Azure App Service/Functions/
        /// Container Apps, AWS ECS/EC2, Azure VM, plain Container, Generic)
        /// keeps the temp-path default.
        /// </summary>
        private static async Task<string> ResolveDiskLoadDirectoryAsync()
        {
            var hardware = await NodeBenchmark.GetHardwareInfoAsync();

            string path = hardware.Environment == "Kubernetes"
                ? Path.Combine(AppContext.BaseDirectory, "diskload")
                : Path.Combine(Path.GetTempPath(), "ScaleTrigger", "diskload");

            Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>
        /// Sysbench's CPU benchmark algorithm: counts primes up to
        /// maxPrime by trial division of each candidate by every integer
        /// from 2 up to its square root.
        /// </summary>
        public static void SimulateCpuLoad(int maxPrime)
        {
            if (maxPrime <= 0)
            {
                return;
            }

            long primeCount = CountPrimesUpTo(maxPrime);
            GC.KeepAlive(primeCount);
        }

        /// <summary>
        /// Sysbench's CPU algorithm: counts primes from 2 to max by trial
        /// division. Shared by SimulateCpuLoad, SqliteRepository's
        /// sysbench_cpu scalar function, and (via IsPrime)
        /// NodeBenchmark.RunCpuBenchmark, whose time-bounded outer loop
        /// can't reuse this directly without redoing all the smaller n's
        /// work on every tick.
        /// </summary>
        internal static long CountPrimesUpTo(long max)
        {
            long primeCount = 0;
            for (long n = 2; n <= max; n++)
            {
                if (IsPrime(n))
                {
                    primeCount++;
                }
            }

            return primeCount;
        }

        internal static bool IsPrime(long n)
        {
            for (long t = 2; t * t <= n; t++)
            {
                if (n % t == 0)
                {
                    return false;
                }
            }

            return true;
        }

        public static void SimulateMemoryLoad(int kilobytes)
        {
            if (kilobytes <= 0)
            {
                return;
            }

            long sizeInBytes = (long)kilobytes * 1024;
            byte[] buffer = new byte[sizeInBytes];

            Random.Shared.NextBytes(buffer);

            // Touch every page so the OS actually reserves physical memory, not just address space.
            long checksum = 0;
            for (int i = 0; i < buffer.Length; i += 4096)
            {
                checksum += buffer[i];
            }

            GC.KeepAlive(buffer);
            GC.KeepAlive(checksum);
        }

        /// <summary>Each call uses its own uniquely-named file so concurrent votes don't serialize on a shared one.</summary>
        public static async Task SimulateDiskLoad(int kilobytes)
        {
            if (kilobytes <= 0)
            {
                return;
            }

            long sizeInBytes = (long)kilobytes * 1024;
            byte[] buffer = new byte[sizeInBytes];
            Random.Shared.NextBytes(buffer);

            string directory = await DiskLoadDirectory.Value;
            string path = Path.Combine(directory, $"{Guid.NewGuid():N}.tmp");

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
                File.Delete(path);
            }
        }

        /// <summary>Task.Delay, not a blocking sleep, so the request thread is freed for the wait.</summary>
        public static Task SimulateNetworkLatencyAsync(int milliseconds)
        {
            return milliseconds > 0 ? Task.Delay(milliseconds) : Task.CompletedTask;
        }
    }
}
