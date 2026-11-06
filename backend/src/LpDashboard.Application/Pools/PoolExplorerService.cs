using System.Collections.Concurrent;
using LpDashboard.Application.Abstractions;
using LpDashboard.Domain;
using LpDashboard.Domain.Entities;
using LpDashboard.Domain.Metrics;
using Microsoft.Extensions.Logging;

namespace LpDashboard.Application.Pools;

public sealed record PoolDto(
    string Id,
    Chain Chain,
    Protocol Protocol,
    string Symbol,
    string Token0,
    string Token1,
    decimal? FeePercent,
    int? TickSpacing,
    decimal TvlUsd,
    decimal? Volume1dUsd,
    decimal? Volume7dAvgUsd,
    decimal? VolumeToTvl,
    decimal? FeeApr1d,
    decimal? FeeApr7d,
    decimal? RewardApr,
    bool Held,
    /// <summary>Half-width in percent of the range of the first open position the owner has in this pool, as if centred on the price.</summary>
    double? MyHalfWidthPercent);

public sealed record PoolListDto(IReadOnlyList<PoolDto> Pools, int Total, DateTime FetchedAt);

public sealed record PoolQuery(
    Chain Chain = Chain.Base,
    Protocol? Protocol = null,
    decimal MinTvlUsd = 0,
    string? Sort = null,
    int Limit = 200,
    IReadOnlyCollection<string>? Owners = null);

/// <param name="FeePercent">
/// Overrides the source's fee tier. The list already carries the on-chain fee for pools the owner holds, so the client
/// sends it back here; the analytics source can list a different fee for pools with dynamic fees.
/// </param>
public sealed record EstimateRequest(double HalfWidthPercent, double CapitalUsd, int Days, decimal? FeePercent = null, IReadOnlyDictionary<string, decimal>? KnownFees = null);

/// <summary>What an owner's open position in a pool says about it: its range and the pool's on-chain fee.</summary>
internal sealed record HeldInfo(double HalfWidthPercent, decimal? FeePercent);

public sealed record PoolEstimateDto(
    string PoolId,
    string Basis,
    double Volatility,
    double ReferenceHalfWidthPercent,
    double Concentration,
    double TimeInRange,
    double Apr,
    double AprLow,
    double AprHigh,
    double FeesUsd,
    double DepositSharePercent,
    decimal? RewardApr,
    /// <summary>Price of token0 in token1 now, and the edges of the chosen range around it.</summary>
    double PriceCurrent,
    double PriceLower,
    double PriceUpper,
    /// <summary>Fees over the holding period as a percent of capital.</summary>
    double FeesPercentOfCapital,
    /// <summary>How far the price can fall (positive percent) before impermanent loss equals those fees; null when it never does.</summary>
    double? BreakEvenDownPercent,
    double? BreakEvenUpPercent,
    PoolHistoryDto History,
    IReadOnlyList<PoolAlternativeDto> Alternatives);

/// <param name="FirstExitDay">1-based day of the first close outside the range, or null if the price never left it.</param>
public sealed record PoolHistoryDto(int DaysInRange, int Days, int? FirstExitDay, double LowPercent, double HighPercent);

/// <summary>Another pool for the same token pair, estimated at the same range and holding period.</summary>
public sealed record PoolAlternativeDto(
    string Id, Protocol Protocol, decimal? FeePercent, int? TickSpacing, decimal TvlUsd, double Apr, bool IsSelected);

public enum EstimateFailure { PoolNotFound, NoPriceHistory, NoVolumeData }

public sealed class EstimateException(EstimateFailure failure, string message) : Exception(message)
{
    public EstimateFailure Failure { get; } = failure;
}

/// <summary>Lists pools from the analytics source (cached) and estimates the fee APR of a range in one of them.</summary>
public sealed class PoolExplorerService(
    IPoolStatsSource source,
    IPriceSource prices,
    IPositionRepository positions,
    TimeProvider time,
    ILogger<PoolExplorerService> logger)
{
    private static readonly TimeSpan StatsTtl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan SeriesTtl = TimeSpan.FromHours(6);
    private const int VolatilityDays = 30;
    private const double MaxHorizonDays = 365;

    private static readonly SemaphoreSlim FetchLock = new(1, 1);
    private static readonly ConcurrentDictionary<Chain, (IReadOnlyList<PoolStats> Pools, DateTimeOffset At)> StatsCache = new();
    private static readonly ConcurrentDictionary<string, (decimal? Apr, DateTimeOffset At)> Apr30Cache = new();
    private static readonly ConcurrentDictionary<string, (IReadOnlyList<double> Prices, DateTimeOffset At)> SeriesCache = new();

    public async Task<PoolListDto> ListAsync(PoolQuery query, CancellationToken ct)
    {
        var (all, fetchedAt) = await GetStatsAsync(query.Chain, ct);
        var held = await HeldRangesAsync(query, ct);

        // Pools the owner holds are always listed, whatever the TVL filter, so they never disappear from the page.
        var rows = all
            .Where(p => query.Protocol is null || p.Protocol == query.Protocol)
            .Select(p =>
            {
                if (!held.TryGetValue(MatchKey(p.Protocol, p.Token0, p.Token1, p.FeeHundredthsBip, p.TickSpacing), out var h)) return ToDto(p, null);
                // The position's on-chain fee beats the source's, which can be stale for dynamic-fee pools.
                return ToDto(h.FeePercent is { } fee ? p.WithFee(fee) : p, h.HalfWidthPercent);
            })
            .Where(p => p.Held || p.TvlUsd >= query.MinTvlUsd)
            .ToList();

        IEnumerable<PoolDto> sorted = query.Sort?.ToLowerInvariant() switch
        {
            "tvl" => rows.OrderByDescending(p => p.Held).ThenByDescending(p => p.TvlUsd),
            "volume" => rows.OrderByDescending(p => p.Held).ThenByDescending(p => p.Volume1dUsd ?? 0),
            "feeapr1d" => rows.OrderByDescending(p => p.Held).ThenByDescending(p => p.FeeApr1d ?? -1),
            _ => rows.OrderByDescending(p => p.Held).ThenByDescending(p => p.FeeApr7d ?? p.FeeApr1d ?? -1),
        };

        return new PoolListDto(sorted.Take(Math.Clamp(query.Limit, 1, 1000)).ToList(), rows.Count, fetchedAt.UtcDateTime);
    }

    public async Task<PoolEstimateDto> EstimateAsync(string poolId, EstimateRequest request, CancellationToken ct)
    {
        var halfWidth = Math.Clamp(request.HalfWidthPercent / 100, AprEstimator.MinHalfWidth, AprEstimator.MaxHalfWidth);
        var days = (int)Math.Clamp(request.Days, 1, MaxHorizonDays);
        var capital = Math.Max(0, request.CapitalUsd);

        var pool = FindCached(poolId) ?? throw new EstimateException(EstimateFailure.PoolNotFound, "Pool not found. Reload the pool list and try again.");
        pool = await WithFeeApr30dAsync(pool, ct);
        if (request.FeePercent is > 0 and <= 10) pool = pool.WithFee(request.FeePercent.Value);
        if (Rates(pool) is not var (basis, rateLow, rateHigh, basisLabel))
            throw new EstimateException(EstimateFailure.NoVolumeData, "This pool has no volume data, so its fee APR cannot be estimated.");

        var series = await GetPriceSeriesAsync(pool, ct);
        var volatility = AprEstimator.AnnualisedVolatility(series);
        var e = AprEstimator.Estimate(basis, rateLow, rateHigh, halfWidth, volatility, days, capital, (double)pool.TvlUsd);

        var price = series[^1];
        var feeFraction = e.Apr / 100 * days / 365;
        var history = AprEstimator.Backtest(series, halfWidth);

        return new PoolEstimateDto(
            pool.Id, basisLabel, volatility, e.ReferenceHalfWidth * 100, e.Concentration, e.TimeInRange,
            e.Apr, e.AprLow, e.AprHigh, capital * e.Apr / 100 * days / 365, e.DepositShare * 100, pool.RewardApr,
            price, price * (1 - halfWidth), price * (1 + halfWidth), feeFraction * 100,
            AprEstimator.BreakEvenMove(halfWidth, feeFraction, up: false) * 100,
            AprEstimator.BreakEvenMove(halfWidth, feeFraction, up: true) * 100,
            new PoolHistoryDto(history.DaysInRange, history.Days, history.FirstExitDay, history.LowMove * 100, history.HighMove * 100),
            await AlternativesAsync(pool, volatility, halfWidth, days, capital, request.KnownFees, ct));
    }

    /// <summary>
    /// Pool-wide rates (swap-fee APR on the best window available, and the spot emission rate) for the pool each open
    /// position is in, keyed by position id. Positions whose pool the analytics source does not list are left out, and a
    /// source outage yields fewer entries rather than an error.
    /// </summary>
    public async Task<IReadOnlyDictionary<int, PoolRates>> GetPoolRatesAsync(IEnumerable<Position> open, CancellationToken ct)
    {
        var result = new Dictionary<int, PoolRates>();
        var byPool = new Dictionary<string, PoolRates?>();

        foreach (var chain in open.Where(p => p.Pool is not null).GroupBy(p => p.Chain))
        {
            IReadOnlyList<PoolStats> stats;
            try
            {
                stats = (await GetStatsAsync(chain.Key, ct)).Pools;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Pool analytics unavailable for {Chain}; skipping the pool comparison", chain.Key);
                continue;
            }

            var listed = new Dictionary<string, PoolStats>();
            foreach (var s in stats) listed.TryAdd(MatchKey(s.Protocol, s.Token0, s.Token1, s.FeeHundredthsBip, s.TickSpacing), s);

            foreach (var p in chain)
            {
                var pool = p.Pool!;
                var key = MatchKey(p.Protocol, pool.Token0.Address, pool.Token1.Address, pool.Fee, pool.TickSpacing);
                if (!listed.TryGetValue(key, out var stat)) continue;

                if (!byPool.TryGetValue(stat.Id, out var rates))
                {
                    try
                    {
                        // The position's on-chain fee beats the source's, which can be stale for dynamic-fee pools.
                        var fixedFee = pool.Fee is { } f ? stat.WithFee(f / 10000m) : stat;
                        var withHistory = await WithFeeApr30dAsync(fixedFee, ct);
                        var r = Rates(withHistory);
                        rates = new PoolRates(r is { } x ? (decimal)x.Basis : null, r?.Label, stat.RewardApr);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogWarning(ex, "Could not load rates for pool {PoolId}", stat.Id);
                        rates = null;
                    }
                    byPool[stat.Id] = rates;
                }

                if (rates is not null) result[p.Id] = rates;
            }
        }
        return result;
    }

    /// <summary>The same token pair in other pools, estimated at the same range; the pool being estimated is always included.</summary>
    private async Task<IReadOnlyList<PoolAlternativeDto>> AlternativesAsync(PoolStats selected, double volatility, double halfWidth, int days, double capital, IReadOnlyDictionary<string, decimal>? knownFees, CancellationToken ct)
    {
        const decimal MinTvlUsd = 250_000;
        const int Max = 8;
        var pair = PairKey(selected);
        if (!StatsCache.TryGetValue(selected.Chain, out var cached)) return [];

        var result = new List<PoolAlternativeDto>();
        var others = cached.Pools.Where(p => p.Id != selected.Id && PairKey(p) == pair && p.TvlUsd >= MinTvlUsd).ToList();
        var withHistory = await Task.WhenAll(others.Select(p => WithFeeApr30dAsync(p, ct)));
        foreach (var listed in withHistory.Prepend(selected))
        {
            // A held pool's on-chain fee beats the source's (dynamic-fee pools), as in the pool list.
            var p = listed.Id != selected.Id && knownFees is not null && knownFees.TryGetValue(listed.Id, out var fee) && fee is > 0 and <= 10
                ? listed.WithFee(fee)
                : listed;
            if (Rates(p) is not var (basis, rateLow, rateHigh, _)) continue;
            var est = AprEstimator.Estimate(basis, rateLow, rateHigh, halfWidth, volatility, days, capital, (double)p.TvlUsd);
            result.Add(new PoolAlternativeDto(p.Id, p.Protocol, p.FeePercent, p.TickSpacing, p.TvlUsd, est.Apr, p.Id == selected.Id));
        }

        var top = result.OrderByDescending(a => a.Apr).Take(Max).ToList();
        if (top.All(a => !a.IsSelected) && result.FirstOrDefault(a => a.IsSelected) is { } self) top[^1] = self;
        return top.OrderByDescending(a => a.Apr).ToList();
    }

    /// <summary>The rate an estimate is based on (30-day mean when there is history, else 7-day, else 24h) and the lowest and highest of the rates known.</summary>
    private static (double Basis, double Low, double High, string Label)? Rates(PoolStats p)
    {
        var all = new[] { p.FeeApr30d, p.FeeApr7d, p.FeeApr1d }.Where(r => r.HasValue).Select(r => (double)r!.Value).ToArray();
        if (all.Length == 0) return null;
        var label = p.FeeApr30d.HasValue ? "30d" : p.FeeApr7d.HasValue ? "7d" : "24h";
        return ((double)(p.FeeApr30d ?? p.FeeApr7d ?? p.FeeApr1d)!.Value, all.Min(), all.Max(), label);
    }

    /// <summary>Fills in the 30-day fee APR from the pool's history (cached); without it the pool keeps its 7-day and 24h rates.</summary>
    private async Task<PoolStats> WithFeeApr30dAsync(PoolStats pool, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        if (!Apr30Cache.TryGetValue(pool.Id, out var cached) || now - cached.At >= SeriesTtl)
        {
            decimal? apr = null;
            try
            {
                apr = await source.GetFeeApr30dAsync(pool.Id, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Could not load fee history for pool {PoolId}; using its 7-day and 24h rates", pool.Id);
                if (cached.At == default) return pool;
                apr = cached.Apr;
            }
            cached = (apr, now);
            Apr30Cache[pool.Id] = cached;
        }
        return cached.Apr is { } a ? pool with { FeeApr30d = a } : pool;
    }

    private static string PairKey(PoolStats p)
    {
        var (lo, hi) = string.CompareOrdinal(p.Token0, p.Token1) <= 0 ? (p.Token0, p.Token1) : (p.Token1, p.Token0);
        return $"{lo}|{hi}";
    }

    private static PoolDto ToDto(PoolStats p, double? myHalfWidthPercent) => new(
        p.Id, p.Chain, p.Protocol, p.Symbol, p.Token0, p.Token1, p.FeePercent, p.TickSpacing, p.TvlUsd,
        p.Volume1dUsd, p.Volume7dAvgUsd, p.Volume1dUsd / p.TvlUsd, p.FeeApr1d, p.FeeApr7d, p.RewardApr,
        myHalfWidthPercent.HasValue, myHalfWidthPercent);

    private async Task<(IReadOnlyList<PoolStats> Pools, DateTimeOffset At)> GetStatsAsync(Chain chain, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        if (StatsCache.TryGetValue(chain, out var cached) && now - cached.At < StatsTtl) return cached;

        await FetchLock.WaitAsync(ct);
        try
        {
            if (StatsCache.TryGetValue(chain, out cached) && now - cached.At < StatsTtl) return cached;
            try
            {
                var fresh = (await source.GetPoolsAsync(chain, ct), time.GetUtcNow());
                StatsCache[chain] = fresh;
                return fresh;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                // Serve the last good copy rather than an empty page when the source is down.
                if (cached.Pools is not null)
                {
                    logger.LogWarning(ex, "Pool analytics source failed; serving cached data from {At}", cached.At);
                    return cached;
                }
                throw;
            }
        }
        finally
        {
            FetchLock.Release();
        }
    }

    private static PoolStats? FindCached(string id) =>
        StatsCache.Values.SelectMany(v => v.Pools).FirstOrDefault(p => p.Id == id);

    /// <summary>Match key of every pool the owners hold an open position in, with that position's range half-width in percent.</summary>
    private async Task<Dictionary<string, HeldInfo>> HeldRangesAsync(PoolQuery query, CancellationToken ct)
    {
        var held = new Dictionary<string, HeldInfo>();
        if (query.Owners is not { Count: > 0 } owners) return held;

        var open = await positions.QueryAsync(new PositionFilter(owners, PositionStatus.Open, query.Protocol, query.Chain), ct);
        foreach (var p in open.Where(p => p.Pool is not null))
        {
            var key = MatchKey(p.Protocol, p.Pool!.Token0.Address, p.Pool.Token1.Address, p.Pool.Fee, p.Pool.TickSpacing);
            held.TryAdd(key, new HeldInfo(
                AprEstimator.HalfWidthFromTicks(p.TickLower, p.TickUpper) * 100,
                p.Pool.Fee is { } f ? f / 10000m : null));
        }
        return held;
    }

    /// <summary>
    /// Uniswap pools are told apart by token pair and fee, Slipstream pools by token pair and tick spacing (their fee
    /// can be dynamic, so the source's fee is not a reliable key there).
    /// </summary>
    public static string MatchKey(Protocol protocol, string token0, string token1, int? fee, int? tickSpacing)
    {
        var a = token0.ToLowerInvariant();
        var b = token1.ToLowerInvariant();
        var (lo, hi) = string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);
        var tier = protocol == Protocol.AerodromeSlipstream ? $"ts{tickSpacing}" : $"fee{fee}";
        return $"{protocol}|{lo}|{hi}|{tier}";
    }

    /// <summary>Daily price of token0 in token1 over the last 30 days, oldest first.</summary>
    // Keyed by tokens, not pool, so every pool of a pair is measured on the same price history and volatility.
    private static string SeriesKey(PoolStats p) => $"{p.Chain}:{p.Token0}:{p.Token1}";

    private async Task<IReadOnlyList<double>> GetPriceSeriesAsync(PoolStats pool, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        if (SeriesCache.TryGetValue(SeriesKey(pool), out var cached) && now - cached.At < SeriesTtl) return cached.Prices;

        var chainKey = pool.Chain.ToString().ToLowerInvariant();
        string[] keys = [$"{chainKey}:{pool.Token0}", $"{chainKey}:{pool.Token1}"];
        var start = now.AddDays(-VolatilityDays).ToUnixTimeSeconds();
        var charts = await prices.GetChartAsync(keys, start, 86400, VolatilityDays + 1, ct);

        if (!charts.TryGetValue(keys[0], out var s0) || !charts.TryGetValue(keys[1], out var s1))
            throw new EstimateException(EstimateFailure.NoPriceHistory, "No price history for this pool's tokens, so its volatility is unknown.");

        // Pair the two series by day; the price of token0 in token1 is what moves the pool.
        var byDay1 = s1.ToDictionary(p => p.Timestamp / 86400, p => p.Price);
        var ratios = s0.Where(p => byDay1.TryGetValue(p.Timestamp / 86400, out var d) && d > 0)
            .Select(p => (double)(p.Price / byDay1[p.Timestamp / 86400]))
            .ToList();
        if (ratios.Count < 10)
            throw new EstimateException(EstimateFailure.NoPriceHistory, "Too little price history for this pool's tokens to measure volatility.");

        SeriesCache[SeriesKey(pool)] = (ratios, now);
        return ratios;
    }
}
