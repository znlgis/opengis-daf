using System.Collections.Concurrent;
using OpenGisDAF.Core;

namespace OpenGisDAF.Execution;

public sealed class ResultCache : IResultCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _keyLocks = new();
    private readonly TimeSpan _defaultTtl = TimeSpan.FromHours(1);
    private readonly object _lifecycleSync = new();
    private TaskCompletionSource _operationsDrained = CreateCompletedSource();
    private int _activeOperations;
    private bool _clearing;
    private const int MaxEntries = 1000;

    public async Task<T?> GetOrComputeAsync<T>(string cacheKey, Func<Task<T>> factory, TimeSpan? ttl = null)
    {
        await EnterOperationAsync(CancellationToken.None);

        try
        {
            return await GetOrComputeCoreAsync(cacheKey, factory, ttl);
        }
        finally
        {
            ExitOperation();
        }
    }

    private async Task<T?> GetOrComputeCoreAsync<T>(string cacheKey, Func<Task<T>> factory, TimeSpan? ttl)
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
            var replacement = new CacheEntry(value!, DateTimeOffset.UtcNow);
            if (_cache.TryGetValue(cacheKey, out var previous) && _cache.TryUpdate(cacheKey, replacement, previous))
                await DisposeValueAsync(previous.Value);
            else
                _cache[cacheKey] = replacement;

            // Enforce max entries by evicting oldest expired entries
            await EvictIfNeededAsync();

            return value;
        }
        finally
        {
            keyLock.Release();
        }
    }

    public async Task InvalidateAsync(string cacheKeyPrefix)
    {
        await EnterOperationAsync(CancellationToken.None);

        try
        {
            await InvalidateCoreAsync(cacheKeyPrefix);
        }
        finally
        {
            ExitOperation();
        }
    }

    private async Task InvalidateCoreAsync(string cacheKeyPrefix)
    {
        var keysToRemove = new List<string>();
        foreach (var kvp in _cache)
        {
            if (kvp.Key.StartsWith(cacheKeyPrefix, StringComparison.OrdinalIgnoreCase))
                keysToRemove.Add(kvp.Key);
        }

        foreach (var key in keysToRemove)
        {
            if (_cache.TryRemove(key, out var removed))
                await DisposeValueAsync(removed.Value);
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await BeginClearAsync(cancellationToken);

        try
        {
            var entries = _cache.ToArray();
            _cache.Clear();

            foreach (var entry in entries)
            {
                if (entry.Value.Value is IAsyncDisposable disposable)
                    await disposable.DisposeAsync();
            }

            cancellationToken.ThrowIfCancellationRequested();
            RemoveIdleKeyLocks();
        }
        finally
        {
            EndClear();
        }
    }

    private async Task EvictIfNeededAsync()
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
        {
            if (_cache.TryRemove(key, out var removed))
                await DisposeValueAsync(removed.Value);
        }

        // Second pass: if still over limit, evict oldest entries regardless of TTL
        if (_cache.Count > MaxEntries)
        {
            var sorted = _cache.OrderBy(kvp => kvp.Value.CreatedAt).ToList();
            var excess = _cache.Count - MaxEntries;
            for (var i = 0; i < excess; i++)
            {
                if (_cache.TryRemove(sorted[i].Key, out var removed))
                    await DisposeValueAsync(removed.Value);
            }
        }
    }

    private async Task EnterOperationAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Task waitTask;
            lock (_lifecycleSync)
            {
                if (!_clearing)
                {
                    if (_activeOperations == 0)
                        _operationsDrained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _activeOperations++;
                    return;
                }

                waitTask = _operationsDrained.Task;
            }

            await waitTask.WaitAsync(cancellationToken);
        }
    }

    private async Task BeginClearAsync(CancellationToken cancellationToken)
    {
        Task waitTask;
        lock (_lifecycleSync)
        {
            if (_clearing)
                throw new InvalidOperationException("Cache clearing is already in progress.");

            _clearing = true;
            waitTask = _operationsDrained.Task;
        }

        try
        {
            await waitTask.WaitAsync(cancellationToken);
        }
        catch
        {
            EndClear();
            throw;
        }
    }

    private void ExitOperation()
    {
        lock (_lifecycleSync)
        {
            _activeOperations--;
            if (_activeOperations == 0)
                _operationsDrained.TrySetResult();
        }
    }

    private void EndClear()
    {
        lock (_lifecycleSync)
        {
            _clearing = false;
            if (_activeOperations == 0)
                _operationsDrained = CreateCompletedSource();
        }
    }

    private void RemoveIdleKeyLocks()
    {
        foreach (var pair in _keyLocks)
        {
            if (_keyLocks.TryRemove(pair.Key, out var keyLock))
                keyLock.Dispose();
        }
    }

    private static TaskCompletionSource CreateCompletedSource()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }

    private static async Task DisposeValueAsync(object value)
    {
        if (value is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
    }

    private sealed record CacheEntry(object Value, DateTimeOffset CreatedAt);
}
