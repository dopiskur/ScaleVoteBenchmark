using System.Security.Cryptography;

namespace ScaleVoteBenchmark.Api
{
    /// <summary>
    /// Helper class that simulates CPU and memory load while processing a
    /// single vote. Load intensity is defined exclusively through values
    /// in the appsettings.json file, without needing to change the code.
    /// </summary>
    public static class LoadSimulator
    {
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
    }
}
