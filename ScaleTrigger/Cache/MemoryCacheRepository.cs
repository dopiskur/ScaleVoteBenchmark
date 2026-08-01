using Microsoft.Extensions.Caching.Memory;
using ScaleTrigger.Interfaces;

namespace ScaleTrigger.Cache
{
    /// <summary>
    /// Cache implementation using the native .NET "MemoryCache" class.
    /// Used to temporarily store voting results in order to reduce the
    /// number of queries against the data layer.
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
