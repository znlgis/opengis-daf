using System.Collections.Concurrent;
using OpenGisDAF.Core;

namespace OpenGisDAF.Execution;

public sealed class ResultCache : IResultCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly TimeSpan _defaultTtl = TimeSpan.FromHours(1);

    public Task<T?> GetOrComputeAsync<T>(string cacheKey, Func<Task<T>> factory, TimeSpan? ttl = null)
    {
        var effectiveTtl = ttl ?? _defaultTtl;

        if (_cache.TryGetValue(cacheKey, out var entry))
        {
            if (DateTimeOffset.UtcNow - entry.CreatedAt < effectiveTtl)
                return Task.FromResult((T?)entry.Value);
        }

        return ComputeAndStoreAsync(cacheKey, factory, effectiveTtl);
    }

    private async Task<T?> ComputeAndStoreAsync<T>(string cacheKey, Func<Task<T>> factory, TimeSpan ttl)
    {
        var value = await factory();
        _cache[cacheKey] = new CacheEntry(value!, DateTimeOffset.UtcNow);
        return value;
    }

    public Task InvalidateAsync(string cacheKeyPrefix)
    {
        foreach (var key in _cache.Keys)
        {
            if (key.StartsWith(cacheKeyPrefix, StringComparison.OrdinalIgnoreCase))
                _cache.TryRemove(key, out _);
        }
        return Task.CompletedTask;
    }

    private sealed record CacheEntry(object Value, DateTimeOffset CreatedAt);
}
