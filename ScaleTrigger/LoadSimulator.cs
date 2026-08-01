namespace ScaleTrigger
{
    public static class LoadSimulator
    {
        private static readonly string DiskLoadDirectory = CreateDiskLoadDirectory();

        private static string CreateDiskLoadDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "ScaleTrigger", "diskload");
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

            long primeCount = 0;
            for (long n = 2; n <= maxPrime; n++)
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

                if (isPrime)
                {
                    primeCount++;
                }
            }

            GC.KeepAlive(primeCount);
        }

        public static void SimulateMemoryLoad(int megabytes)
        {
            if (megabytes <= 0)
            {
                return;
            }

            int sizeInBytes = megabytes * 1024 * 1024;
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
        public static void SimulateDiskLoad(int kilobytes)
        {
            if (kilobytes <= 0)
            {
                return;
            }

            byte[] buffer = new byte[kilobytes * 1024];
            Random.Shared.NextBytes(buffer);

            string path = Path.Combine(DiskLoadDirectory, $"{Guid.NewGuid():N}.tmp");

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
