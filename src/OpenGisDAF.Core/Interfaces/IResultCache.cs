namespace OpenGisDAF.Core;

public interface IResultCache
{
    Task<T?> GetOrComputeAsync<T>(string cacheKey, Func<Task<T>> factory, TimeSpan? ttl = null);
    Task InvalidateAsync(string cacheKeyPrefix);
    Task ClearAsync(CancellationToken cancellationToken = default);
}
