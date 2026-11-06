using System.Collections.Concurrent;
using LpDashboard.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace LpDashboard.Application.Pricing;

public sealed record TickerPriceDto(string Symbol, decimal PriceUsd, decimal? Change24hPercent);

public sealed record TickerDto(IReadOnlyList<TickerPriceDto> Prices, DateTimeOffset FetchedAt);

/// <summary>Spot prices of a few headline assets for the dashboard header, with the change over the last 24 hours.</summary>
public sealed class MarketTickerService(PriceService prices, IPriceSource source, TimeProvider time, ILogger<MarketTickerService> logger)
{
    /// <summary>Symbol and DefiLlama key of each asset shown, in display order.</summary>
    public static readonly IReadOnlyList<(string Symbol, string Key)> Assets =
    [
        ("BTC", "coingecko:bitcoin"),
        ("ETH", "coingecko:ethereum"),
        ("BNB", "coingecko:binancecoin"),
        ("AERO", "coingecko:aerodrome-finance"),
    ];

    private static readonly TimeSpan ReferenceTtl = TimeSpan.FromMinutes(10);
    private static readonly ConcurrentDictionary<string, (decimal Price, DateTimeOffset At)> ReferenceCache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<TickerDto> GetAsync(CancellationToken ct)
    {
        var keys = Assets.Select(a => a.Key).ToList();
        var current = await prices.GetCurrentAsync(keys, ct);
        var reference = await GetReferenceAsync(keys, ct);

        var items = new List<TickerPriceDto>();
        foreach (var (symbol, key) in Assets)
        {
            if (!current.TryGetValue(key, out var price)) continue;
            items.Add(new TickerPriceDto(symbol, price, Change(price, reference.GetValueOrDefault(key))));
        }
        return new TickerDto(items, time.GetUtcNow());
    }

    public static decimal? Change(decimal current, decimal previous) =>
        previous > 0 ? Math.Round((current - previous) / previous * 100m, 2) : null;

    /// <summary>Prices 24 hours ago. A failure only costs the change figure, so it is logged and the price still shows.</summary>
    private async Task<IReadOnlyDictionary<string, decimal>> GetReferenceAsync(IReadOnlyCollection<string> keys, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        foreach (var key in keys)
        {
            if (ReferenceCache.TryGetValue(key, out var cached) && now - cached.At < ReferenceTtl)
                result[key] = cached.Price;
            else
                missing.Add(key);
        }

        if (missing.Count == 0) return result;
        try
        {
            var fetched = await source.GetHistoricalAsync(missing, now.AddHours(-24).ToUnixTimeSeconds(), ct);
            foreach (var (key, price) in fetched)
            {
                result[key] = price;
                ReferenceCache[key] = (price, now);
            }
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Could not load 24h reference prices for the ticker");
        }
        return result;
    }
}
