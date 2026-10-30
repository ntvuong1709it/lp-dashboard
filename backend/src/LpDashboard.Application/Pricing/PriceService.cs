using System.Collections.Concurrent;
using LpDashboard.Application.Abstractions;
using LpDashboard.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace LpDashboard.Application.Pricing;

public sealed record PriceQuery(string Key, long Timestamp);

/// <summary>Historical prices are bucketed hourly and cached in the database; current prices are cached in memory briefly.</summary>
public sealed class PriceService(IPriceSource source, ITokenPriceRepository repository, TimeProvider time, ILogger<PriceService> logger)
{
    private const long BucketSeconds = 3600;
    private static readonly TimeSpan CurrentTtl = TimeSpan.FromSeconds(60);
    private static readonly ConcurrentDictionary<string, (decimal Price, DateTimeOffset At)> CurrentCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<(string, long), decimal?> _historical = [];

    public async Task<IReadOnlyDictionary<string, decimal>> GetCurrentAsync(IEnumerable<string> keys, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();

        foreach (var key in keys.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (CurrentCache.TryGetValue(key, out var cached) && now - cached.At < CurrentTtl)
                result[key] = cached.Price;
            else
                missing.Add(key);
        }

        if (missing.Count > 0)
        {
            var fetched = await source.GetCurrentAsync(missing, ct);
            foreach (var (key, price) in fetched)
            {
                result[key] = price;
                CurrentCache[key] = (price, now);
            }
        }

        return result;
    }

    public async Task<IReadOnlyDictionary<PriceQuery, decimal?>> GetHistoricalAsync(IEnumerable<PriceQuery> queries, CancellationToken ct)
    {
        var list = queries.Distinct().ToList();
        var buckets = list.Select(q => (Key: q.Key.ToLowerInvariant(), Bucket: Bucket(q.Timestamp))).Distinct().ToList();
        var unresolved = buckets.Where(b => !_historical.ContainsKey(b)).ToList();

        if (unresolved.Count > 0)
        {
            var stored = await repository.FindAsync(
                unresolved.Select(b => b.Key).Distinct().ToList(),
                unresolved.Select(b => b.Bucket).Distinct().ToList(),
                ct);
            foreach (var row in stored)
                _historical[(row.PriceKey, row.Timestamp)] = row.PriceUsd;

            foreach (var group in unresolved.Where(b => !_historical.ContainsKey(b)).GroupBy(b => b.Bucket))
            {
                var keys = group.Select(g => g.Key).ToList();
                IReadOnlyDictionary<string, decimal> fetched;
                try
                {
                    fetched = await source.GetHistoricalAsync(keys, group.Key, ct);
                }
                catch (HttpRequestException ex)
                {
                    logger.LogWarning(ex, "Historical price fetch failed for {Count} tokens at {Timestamp}", keys.Count, group.Key);
                    continue;
                }

                foreach (var key in keys)
                {
                    decimal? price = fetched.TryGetValue(key, out var p) ? p : null;
                    _historical[(key, group.Key)] = price;
                    repository.Add(new TokenPrice { PriceKey = key, Timestamp = group.Key, PriceUsd = price });
                }
            }
        }

        return list.ToDictionary(
            q => q,
            q => _historical.TryGetValue((q.Key.ToLowerInvariant(), Bucket(q.Timestamp)), out var price) ? price : null);
    }

    /// <summary>
    /// Prices at <paramref name="count"/> hourly-aligned points every <paramref name="stepSeconds"/> from <paramref name="start"/>.
    /// Cached buckets are reused; misses are filled from one chart request and cached like other historical prices.
    /// </summary>
    public async Task<IReadOnlyDictionary<PriceQuery, decimal?>> GetSeriesAsync(
        IReadOnlyCollection<string> keys, long start, long stepSeconds, int count, CancellationToken ct)
    {
        start = Bucket(start);
        stepSeconds = Math.Max(BucketSeconds, stepSeconds - stepSeconds % BucketSeconds);
        var normalizedKeys = keys.Select(k => k.ToLowerInvariant()).Distinct().ToList();
        var timestamps = Enumerable.Range(0, Math.Max(0, count)).Select(i => start + i * stepSeconds).ToList();
        var wanted = normalizedKeys.SelectMany(k => timestamps.Select(t => (Key: k, Bucket: t))).ToList();

        var unresolved = wanted.Where(b => !_historical.ContainsKey(b)).ToList();
        if (unresolved.Count > 0)
        {
            var stored = await repository.FindAsync(
                unresolved.Select(b => b.Key).Distinct().ToList(),
                unresolved.Select(b => b.Bucket).Distinct().ToList(),
                ct);
            foreach (var row in stored)
                _historical[(row.PriceKey, row.Timestamp)] = row.PriceUsd;
            unresolved = unresolved.Where(b => !_historical.ContainsKey(b)).ToList();
        }

        if (unresolved.Count > 0)
        {
            var missingKeys = unresolved.Select(b => b.Key).Distinct().ToList();
            var first = unresolved.Min(b => b.Bucket);
            var span = (int)((unresolved.Max(b => b.Bucket) - first) / stepSeconds) + 1;
            try
            {
                var charts = await source.GetChartAsync(missingKeys, first, stepSeconds, span, ct);
                var tolerance = Math.Max(stepSeconds, 6 * BucketSeconds);
                // Very recent prices may not be indexed yet, so only remember misses for older buckets.
                var settled = time.GetUtcNow().ToUnixTimeSeconds() - 24 * BucketSeconds;

                foreach (var (key, bucket) in unresolved)
                {
                    decimal? price = null;
                    if (charts.TryGetValue(key, out var points) && points.Count > 0)
                    {
                        var nearest = points.MinBy(p => Math.Abs(p.Timestamp - bucket))!;
                        if (Math.Abs(nearest.Timestamp - bucket) <= tolerance) price = nearest.Price;
                    }

                    if (price is null && bucket > settled) continue;
                    _historical[(key, bucket)] = price;
                    repository.Add(new TokenPrice { PriceKey = key, Timestamp = bucket, PriceUsd = price });
                }
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "Price chart fetch failed for {Count} tokens from {Start}", missingKeys.Count, first);
            }
        }

        return wanted.ToDictionary(
            b => new PriceQuery(b.Key, b.Bucket),
            b => _historical.TryGetValue(b, out var price) ? price : null);
    }

    private static long Bucket(long timestamp) => timestamp - timestamp % BucketSeconds;
}
