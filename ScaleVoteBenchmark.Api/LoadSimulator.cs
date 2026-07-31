using System.Security.Cryptography;

namespace ScaleVoteBenchmark.Api
{
    /// <summary>
    /// Pomoćna klasa koja simulira procesorsko i memorijsko opterećenje
    /// prilikom obrade jednog glasa. Intenzitet opterećenja definira se
    /// isključivo kroz vrijednosti u appsettings.json datoteci, bez
    /// potrebe za izmjenom koda.
    /// </summary>
    public static class LoadSimulator
    {
        /// <summary>
        /// Simulira procesorsko opterećenje ponavljanim izračunom
        /// SHA-256 hash vrijednosti.
        /// </summary>
        /// <param name="iterations">Broj ponavljanja hash kalkulacije.</param>
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
        /// Simulira memorijsko opterećenje alokacijom bloka podataka
        /// zadane veličine. Blok se u potpunosti popunjava nasumičnim
        /// vrijednostima kako bi se memorijske stranice stvarno
        /// rezervirale, a ne samo virtualno alocirale.
        /// </summary>
        /// <param name="megabytes">Veličina bloka memorije u MB.</param>
        public static void SimulateMemoryLoad(int megabytes)
        {
            if (megabytes <= 0)
            {
                return;
            }

            int sizeInBytes = megabytes * 1024 * 1024;
            byte[] buffer = new byte[sizeInBytes];

            Random.Shared.NextBytes(buffer);

            // Prolazak kroz sve memorijske stranice kako bi se osiguralo
            // da je operacijski sustav stvarno rezervirao fizičku memoriju,
            // a ne samo virtualno alocirale.
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
