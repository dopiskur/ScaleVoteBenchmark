using Microsoft.Extensions.Caching.Memory;
using ScaleVoteBenchmark.Lib.Interfaces;

namespace ScaleVoteBenchmark.Lib.Cache
{
    /// <summary>
    /// Implementacija predmemorije korištenjem nativne .NET klase
    /// "MemoryCache". Koristi se za privremeno spremanje rezultata
    /// glasovanja kako bi se smanjio broj upita prema podatkovnom sloju.
    /// </summary>
    public class MemoryCacheRepository : ICache
    {
        private readonly IMemoryCache cache;

        public MemoryCacheRepository(IMemoryCache cache)
        {
            this.cache = cache;
        }

        public T? GetItem<T>(string key) where T : class
        {
            cache.TryGetValue(key, out T? value);
            return value;
        }

        public void SetItem<T>(string key, T value, int slidingExpirationMinutes) where T : class
        {
            var options = new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(slidingExpirationMinutes)
            };

            cache.Set(key, value, options);
        }

        public void RemoveItem(string key)
        {
            cache.Remove(key);
        }
    }
}
