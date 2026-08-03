using System.Security.Cryptography;

namespace ScaleTrigger
{
    public static class LoadSimulator
    {
        private static readonly Lazy<Task<string>> DiskLoadDirectory = new(ResolveDiskLoadDirectoryAsync);

        /// <summary>Kubernetes commonly mounts /tmp as a memory-backed emptyDir, so disk load there targets the app's working directory instead of Path.GetTempPath().</summary>
        private static async Task<string> ResolveDiskLoadDirectoryAsync()
        {
            var hardware = await NodeBenchmark.GetHardwareInfoAsync();

            string path = hardware.Environment == "Kubernetes"
                ? Path.Combine(AppContext.BaseDirectory, "diskload")
                : Path.Combine(Path.GetTempPath(), "ScaleTrigger", "diskload");

            Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>Chained SHA-512: each hash's output feeds the next, so the JIT can't fold the loop away.</summary>
        public static void SimulateCpuLoad(int iterations)
        {
            if (iterations <= 0)
            {
                return;
            }

            byte[] result = HashIterations(iterations);
            GC.KeepAlive(result);
        }

        /// <summary>Shared by SimulateCpuLoad and SqliteRepository's sysbench_cpu scalar function.</summary>
        internal static byte[] HashIterations(long iterations)
        {
            byte[] buffer = new byte[64];
            Random.Shared.NextBytes(buffer);

            for (long i = 0; i < iterations; i++)
            {
                buffer = SHA512.HashData(buffer);
            }

            return buffer;
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
