using Microsoft.Extensions.Caching.Memory;
using ScaleTrigger.Interfaces;

namespace ScaleTrigger.Cache
{
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
