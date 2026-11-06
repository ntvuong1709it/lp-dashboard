namespace LpDashboard.Application.Pools;

/// <summary>One day of a pool's history. Fees and volume are derived from TVL and the fee APR, not measured.</summary>
public sealed record PoolHistoryPointDto(
    DateOnly Date, decimal TvlUsd, decimal? FeeApr, decimal? RewardApr, decimal? FeesUsd, decimal? VolumeUsd);

/// <param name="FeePercent">The fee the fee figures are at.</param>
/// <param name="Apr30d">Mean fee APR of the days that have one, or null with fewer than 10 such days.</param>
public sealed record PoolHistorySeriesDto(
    string PoolId,
    decimal? FeePercent,
    IReadOnlyList<PoolHistoryPointDto> Points,
    decimal? Apr30d,
    decimal? Fees7dUsd,
    decimal? Fees30dUsd,
    decimal? Volume30dUsd,
    /// <summary>
    /// Set when the pool's TVL was read on-chain instead of from the source: the TVL now, and the one the source reports.
    /// The daily series is the source's own, so days where it differs from the chain are off by that gap.
    /// </summary>
    decimal? TvlUsd = null,
    decimal? SourceTvlUsd = null);

public static class PoolHistory
{
    public const int Days = 30;
    public const int MinDaysForAverage = 10;

    /// <summary>Mean fee APR of the last 30 days that have one, at the fee the source lists; null when there are too few.</summary>
    public static decimal? MeanFeeApr(IReadOnlyList<PoolChartPoint> points)
    {
        var rates = points.Where(p => p.FeeApr.HasValue).Select(p => p.FeeApr!.Value).TakeLast(Days).ToList();
        return rates.Count >= MinDaysForAverage ? rates.Average() : null;
    }

    /// <summary>
    /// The last 30 days as a series. A day's fees are TVL × APR ÷ 365 and its volume is those fees ÷ the fee tier, both
    /// at the fee the source lists. When <paramref name="feePercent"/> differs (a pool's on-chain fee beats the source's),
    /// volume stays as derived and fees and APR are rescaled to the new tier.
    /// </summary>
    public static PoolHistorySeriesDto Build(string poolId, IReadOnlyList<PoolChartPoint> chart, decimal? sourceFeePercent, decimal? feePercent)
    {
        var fee = feePercent ?? sourceFeePercent;
        var scale = sourceFeePercent is > 0 && fee is { } f ? f / sourceFeePercent.Value : 1m;

        var points = chart.TakeLast(Days).Select(p =>
        {
            decimal? volume = p.FeeApr is { } apr && sourceFeePercent is > 0
                ? p.TvlUsd * apr / 100 / 365 / (sourceFeePercent.Value / 100)
                : null;
            decimal? fees = volume is { } v && fee is { } fp ? v * fp / 100 : null;
            return new PoolHistoryPointDto(DateOnly.FromDateTime(p.Date), p.TvlUsd, p.FeeApr * scale, p.RewardApr, fees, volume);
        }).ToList();

        var withFees = points.Where(p => p.FeesUsd.HasValue).ToList();
        decimal? Sum(IEnumerable<decimal?> values, int count) => values.Count(v => v.HasValue) >= Math.Min(count, MinDaysForAverage)
            ? values.Sum(v => v ?? 0)
            : null;

        return new PoolHistorySeriesDto(
            poolId, fee, points,
            MeanFeeApr(chart) * scale,
            Sum(withFees.TakeLast(7).Select(p => p.FeesUsd), 7),
            Sum(withFees.Select(p => p.FeesUsd), Days),
            Sum(points.Select(p => p.VolumeUsd), Days));
    }
}
