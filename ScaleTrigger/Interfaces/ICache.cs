namespace ScaleTrigger.Interfaces
{
    /// <summary>
    /// Interface for accessing the caching mechanism.
    /// </summary>
    public interface ICache
    {
        T? GetItem<T>(string key) where T : class;

        void SetItem<T>(string key, T value, int slidingExpirationMinutes) where T : class;

        void RemoveItem(string key);
    }
}
