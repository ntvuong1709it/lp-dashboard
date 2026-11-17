using System.Collections.Concurrent;

namespace LpDashboard.Application.Market;

public enum CacheState { Fresh, Stale, None }

/// <summary>
/// An in-memory cache for the Market watch's source answers. A value is fresh for its time to live; when a refresh fails,
/// the old value is still served, marked stale, up to a limit. One fetch per key runs at a time. Nothing is persisted:
/// a restart simply fetches again.
/// </summary>
public sealed class MarketCache(TimeProvider time)
{
    private sealed class Entry
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public object? Value;
        public DateTimeOffset At;
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    public Task<(T? Value, CacheState State)> GetOrFetchAsync<T>(
        string key, TimeSpan ttl, TimeSpan staleMax, Func<CancellationToken, Task<T?>> fetch, CancellationToken ct) where T : class =>
        GetOrFetchAsync(key, _ => ttl, staleMax, fetch, ct);

    /// <param name="ttlFor">How long the cached value stays fresh; it may depend on the value (a published result never changes).</param>
    /// <param name="fetch">Returns null when the source did not answer. An exception counts the same.</param>
    public async Task<(T? Value, CacheState State)> GetOrFetchAsync<T>(
        string key, Func<T, TimeSpan> ttlFor, TimeSpan staleMax, Func<CancellationToken, Task<T?>> fetch, CancellationToken ct) where T : class
    {
        var entry = _entries.GetOrAdd(key, _ => new Entry());
        if (Fresh(entry, ttlFor) is { } hit) return (hit, CacheState.Fresh);

        await entry.Gate.WaitAsync(ct);
        try
        {
            // Another caller may have fetched while this one waited for the gate.
            if (Fresh(entry, ttlFor) is { } again) return (again, CacheState.Fresh);

            T? fetched = null;
            try
            {
                fetched = await fetch(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // A failed source is not an error here: the caller gets the old value or nothing.
            }

            var now = time.GetUtcNow();
            if (fetched is not null)
            {
                entry.Value = fetched;
                entry.At = now;
                return (fetched, CacheState.Fresh);
            }

            return entry.Value is T old && now - entry.At <= staleMax ? (old, CacheState.Stale) : (null, CacheState.None);
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    private T? Fresh<T>(Entry entry, Func<T, TimeSpan> ttlFor) where T : class =>
        entry.Value is T v && time.GetUtcNow() - entry.At < ttlFor(v) ? v : null;
}
