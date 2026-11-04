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
    decimal? RewardApr)
{
    /// <summary>Fee in hundredths of a bip, the unit <c>Pool.Fee</c> uses (0.3% = 3000).</summary>
    public int? FeeHundredthsBip => FeePercent is { } f ? (int)Math.Round(f * 10000) : null;

    /// <summary>The same pool with another fee tier; the 24h APR is recomputed from volume, since the source's figure used its own fee.</summary>
    public PoolStats WithFee(decimal feePercent) =>
        this with
        {
            FeePercent = feePercent,
            FeeApr1d = Volume1dUsd is { } v && TvlUsd > 0 ? v * feePercent / 100 * 365 / TvlUsd * 100 : FeeApr1d,
        };

    /// <summary>Average daily volume of the last 7 days.</summary>
    public decimal? Volume7dAvgUsd => Volume7dUsd / 7;

    public decimal? FeeApr7d => Apr(Volume7dAvgUsd);

    /// <summary>Fee APR from a day's volume: volume × fee × 365 ÷ TVL.</summary>
    private decimal? Apr(decimal? dailyVolume) =>
        dailyVolume is { } v && FeePercent is { } f && TvlUsd > 0 ? v * f / 100 * 365 / TvlUsd * 100 : null;
}

public interface IPoolStatsSource
{
    Task<IReadOnlyList<PoolStats>> GetPoolsAsync(Chain chain, CancellationToken ct);
}
