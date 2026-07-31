using System.Security.Cryptography;

namespace ScaleVoteBenchmark.Api
{
    /// <summary>
    /// Helper class that simulates CPU, memory, disk write and network
    /// latency load while processing a single vote. Load intensity is
    /// defined exclusively through values in the appsettings.json file,
    /// without needing to change the code.
    /// </summary>
    public static class LoadSimulator
    {
        private static readonly string DiskLoadDirectory = CreateDiskLoadDirectory();

        private static string CreateDiskLoadDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "ScaleVoteBenchmark", "diskload");
            Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>
        /// Simulates CPU load by repeatedly computing a SHA-256 hash
        /// value.
        /// </summary>
        /// <param name="iterations">Number of hash computation repetitions.</param>
        public static void SimulateCpuLoad(int iterations)
        {
            if (iterations <= 0)
            {
                return;
            }

            using var sha256 = SHA256.Create();
            byte[] data = Guid.NewGuid().ToByteArray();

            for (int i = 0; i < iterations; i++)
            {
                data = sha256.ComputeHash(data);
            }
        }

        /// <summary>
        /// Simulates memory load by allocating a block of data of the
        /// given size. The block is fully filled with random values so
        /// the memory pages are actually reserved, not just virtually
        /// allocated.
        /// </summary>
        /// <param name="megabytes">Size of the memory block in MB.</param>
        public static void SimulateMemoryLoad(int megabytes)
        {
            if (megabytes <= 0)
            {
                return;
            }

            int sizeInBytes = megabytes * 1024 * 1024;
            byte[] buffer = new byte[sizeInBytes];

            Random.Shared.NextBytes(buffer);

            // Walk through all memory pages to ensure the operating
            // system actually reserved physical memory, not just virtual
            // address space.
            long checksum = 0;
            for (int i = 0; i < buffer.Length; i += 4096)
            {
                checksum += buffer[i];
            }

            GC.KeepAlive(buffer);
            GC.KeepAlive(checksum);
        }

        /// <summary>
        /// Simulates disk write load by writing a block of random data to
        /// a temporary file and forcing it to be flushed to disk, then
        /// deleting the file immediately. Each call uses its own
        /// uniquely-named file so concurrent votes generate genuine,
        /// non-contending disk I/O instead of serializing on a shared
        /// file, and nothing accumulates on disk across a long benchmark
        /// run.
        /// </summary>
        /// <param name="kilobytes">Size of the data block to write, in KB.</param>
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

        /// <summary>
        /// Simulates network latency (e.g. a call to a downstream
        /// service) by asynchronously delaying for the given duration.
        /// Uses Task.Delay rather than a blocking sleep, so the request
        /// thread is released back to the pool for the duration of the
        /// "wait" instead of being tied up - the same way a real async
        /// network call would behave, and without artificially limiting
        /// how much concurrent load the app can take.
        /// </summary>
        /// <param name="milliseconds">Delay duration in milliseconds.</param>
        public static Task SimulateNetworkLatencyAsync(int milliseconds)
        {
            return milliseconds > 0 ? Task.Delay(milliseconds) : Task.CompletedTask;
        }
    }
}
