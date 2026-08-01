using ScaleTrigger.Interfaces;

namespace ScaleTrigger.Cache
{
    /// <summary>No-op cache, used when "Cache:Enabled" is false.</summary>
    public class NullCacheRepository : ICache
    {
        public T? GetItem<T>(string key) where T : class => null;

        public void SetItem<T>(string key, T value, int slidingExpirationMinutes) where T : class
        {
        }

        public void RemoveItem(string key)
        {
        }
    }
}
