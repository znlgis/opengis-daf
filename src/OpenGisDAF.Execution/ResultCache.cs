using System.Collections.Concurrent;
using OpenGisDAF.Core;

namespace OpenGisDAF.Execution;

public sealed class ResultCache : IResultCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _keyLocks = new();
    private readonly TimeSpan _defaultTtl = TimeSpan.FromHours(1);
    private const int MaxEntries = 1000;

    public async Task<T?> GetOrComputeAsync<T>(string cacheKey, Func<Task<T>> factory, TimeSpan? ttl = null)
    {
        var effectiveTtl = ttl ?? _defaultTtl;

        // Fast path: cache hit — read without lock
        if (_cache.TryGetValue(cacheKey, out var entry))
        {
            if (DateTimeOffset.UtcNow - entry.CreatedAt < effectiveTtl)
                return (T?)entry.Value;
        }

        // Slow path: compute under per-key lock to prevent duplicate computation
        var keyLock = _keyLocks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await keyLock.WaitAsync();

        try
        {
            // Double-check after acquiring lock
            if (_cache.TryGetValue(cacheKey, out entry))
            {
                if (DateTimeOffset.UtcNow - entry.CreatedAt < effectiveTtl)
                    return (T?)entry.Value;
            }

            var value = await factory();
            _cache[cacheKey] = new CacheEntry(value!, DateTimeOffset.UtcNow);

            // Enforce max entries by evicting oldest expired entries
            EvictIfNeeded();

            return value;
        }
        finally
        {
            keyLock.Release();
        }
    }

    public async Task InvalidateAsync(string cacheKeyPrefix)
    {
        var keysToRemove = new List<string>();
        foreach (var kvp in _cache)
        {
            if (kvp.Key.StartsWith(cacheKeyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                keysToRemove.Add(kvp.Key);
                if (kvp.Value.Value is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync();
            }
        }

        foreach (var key in keysToRemove)
            _cache.TryRemove(key, out _);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in _cache.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entry.Value is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
        }

        _cache.Clear();
    }

    private void EvictIfNeeded()
    {
        if (_cache.Count <= MaxEntries)
            return;

        var now = DateTimeOffset.UtcNow;

        // First pass: remove expired entries
        var toRemove = new List<string>();
        foreach (var kvp in _cache)
        {
            if (now - kvp.Value.CreatedAt >= _defaultTtl)
                toRemove.Add(kvp.Key);
        }

        foreach (var key in toRemove)
            _cache.TryRemove(key, out _);

        // Second pass: if still over limit, evict oldest entries regardless of TTL
        if (_cache.Count > MaxEntries)
        {
            var sorted = _cache.OrderBy(kvp => kvp.Value.CreatedAt).ToList();
            var excess = _cache.Count - MaxEntries;
            for (var i = 0; i < excess; i++)
                _cache.TryRemove(sorted[i].Key, out _);
        }
    }

    private sealed record CacheEntry(object Value, DateTimeOffset CreatedAt);
}
