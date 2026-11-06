using LpDashboard.Domain;

namespace LpDashboard.Application.Pools;

/// <summary>Pool-wide numbers from an analytics source. APRs are percent; volumes and TVL are USD.</summary>
public sealed record PoolStats(
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
    decimal? Volume7dUsd,
    decimal? FeeApr1d,
    decimal? RewardApr,
    /// <summary>Mean daily fee APR of the last 30 days at <see cref="FeePercent"/>; the list endpoint has no 30-day volume, so it is filled in from the pool's history.</summary>
    decimal? FeeApr30d = null,
    /// <summary>The TVL the source reported, when <see cref="TvlUsd"/> was replaced by an on-chain reading.</summary>
    decimal? SourceTvlUsd = null)
{
    /// <summary>
    /// The same pool with its TVL replaced (the analytics source can be far off for a single pool). The pool earns the same
    /// fees on a different base, so the current APRs scale by the old TVL over the new one. The 30-day mean is dropped: it
    /// averages days recorded at TVLs the source got differently wrong, so no single ratio corrects it, and estimates fall
    /// back to the 7-day and 24h rates, which follow the new TVL.
    /// </summary>
    public PoolStats WithTvl(decimal tvlUsd)
    {
        if (tvlUsd <= 0 || tvlUsd == TvlUsd) return this;
        var scale = TvlUsd / tvlUsd;
        return this with
        {
            SourceTvlUsd = SourceTvlUsd ?? TvlUsd,
            TvlUsd = tvlUsd,
            FeeApr1d = FeeApr1d * scale,
            FeeApr30d = null,
            RewardApr = RewardApr * scale,
        };
    }

    /// <summary>Fee in hundredths of a bip, the unit <c>Pool.Fee</c> uses (0.3% = 3000).</summary>
    public int? FeeHundredthsBip => FeePercent is { } f ? (int)Math.Round(f * 10000) : null;

    /// <summary>The same pool with another fee tier; the 24h APR is recomputed from volume, since the source's figure used its own fee.</summary>
    public PoolStats WithFee(decimal feePercent) =>
        this with
        {
            FeePercent = feePercent,
            FeeApr1d = Volume1dUsd is { } v && TvlUsd > 0 ? v * feePercent / 100 * 365 / TvlUsd * 100 : FeeApr1d,
            FeeApr30d = FeeApr30d is { } a && FeePercent is > 0 ? a * feePercent / FeePercent : FeeApr30d,
        };

    /// <summary>Average daily volume of the last 7 days.</summary>
    public decimal? Volume7dAvgUsd => Volume7dUsd / 7;

    public decimal? FeeApr7d => Apr(Volume7dAvgUsd);

    /// <summary>Swap fees paid to the pool over the last 7 days: volume × fee.</summary>
    public decimal? Fees7dUsd => Volume7dUsd is { } v && FeePercent is { } f ? v * f / 100 : null;

    /// <summary>Fee APR from a day's volume: volume × fee × 365 ÷ TVL.</summary>
    private decimal? Apr(decimal? dailyVolume) =>
        dailyVolume is { } v && FeePercent is { } f && TvlUsd > 0 ? v * f / 100 * 365 / TvlUsd * 100 : null;
}

/// <summary>One day of a pool's history from the analytics source. APRs are percent at the fee the source lists.</summary>
public sealed record PoolChartPoint(DateTime Date, decimal TvlUsd, decimal? FeeApr, decimal? RewardApr);

public interface IPoolStatsSource
{
    Task<IReadOnlyList<PoolStats>> GetPoolsAsync(Chain chain, CancellationToken ct);

    /// <summary>Daily history of the pool (TVL and APRs), oldest first; empty when the source has none.</summary>
    Task<IReadOnlyList<PoolChartPoint>> GetChartAsync(string poolId, CancellationToken ct);
}
