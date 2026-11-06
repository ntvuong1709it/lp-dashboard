namespace LpDashboard.Domain.Metrics;

/// <summary>A position's earnings over the last <paramref name="Days"/> complete days, as an annualised rate on the capital it had in that window.</summary>
/// <param name="TotalAprPercent">Swap fees plus reward tokens, claimed or not.</param>
/// <param name="SwapFeeAprPercent">Swap fees only.</param>
public sealed record TrailingApr(int Days, decimal TotalAprPercent, decimal SwapFeeAprPercent, decimal EarnedUsd, decimal AverageCapitalUsd);

/// <summary>Capital snapshot used to average the value a position had inside a window.</summary>
public sealed record ValueSample(DateTime Timestamp, decimal ValueUsd);

/// <param name="GapUsd">LP minus HODL today (fees and gas included); negative means holding would have been better.</param>
/// <param name="DailyEarningsUsd">The rate used for the projection.</param>
/// <param name="RateBasis">Where the rate comes from: "30d" or "lifetime".</param>
/// <param name="DaysToBreakEven">0 when already ahead; null when the gap never closes at this rate.</param>
public sealed record HodlBreakEven(decimal GapUsd, decimal DailyEarningsUsd, string RateBasis, double? DaysToBreakEven);

/// <summary>
/// Your position against the pool's own average LP. The pool figure is what liquidity in that pool earns per dollar
/// (fees ÷ TVL, or for staked Slipstream positions the emission rate); a ratio above 1 means the range beat the pool.
/// </summary>
/// <param name="PoolAprPercent">Pool APR on the same kind of income the position earns.</param>
/// <param name="PoolBasis">Window of the pool figure ("30d", "7d", "24h") or "now" for a spot reward rate.</param>
/// <param name="PositionAprPercent">Position APR on the matching window.</param>
/// <param name="PositionBasis">"30d", "7d" or "lifetime".</param>
/// <param name="IncomeKind">"fees" or "rewards": which side of the pool's income the comparison uses.</param>
public sealed record PoolComparison(
    decimal PoolAprPercent, string PoolBasis, decimal PositionAprPercent, string PositionBasis, string IncomeKind, double Ratio);

public static class PerformanceCalculator
{
    /// <summary>
    /// A rate per day in range divides by the time in range, so a share near zero inflates it without limit. It is only
    /// given when even the lowest plausible share (the estimate's low end) is at least this much.
    /// </summary>
    public const double MinTimeInRangePercent = 20;

    /// <summary>Time in range must cover at least this share of the position's age for the in-range rate to mean anything.</summary>
    public const double MinCoverage = 0.5;

    /// <summary>
    /// Trailing rate over the <paramref name="window"/> complete days before <paramref name="today"/>. Null when the
    /// position is not older than the window (the rate would be a lifetime rate in disguise) or has no capital to measure against.
    /// </summary>
    public static TrailingApr? Trailing(
        IReadOnlyList<DailyEarnings> days, IReadOnlyList<ValueSample> values, decimal currentValueUsd,
        double? ageDays, DateOnly today, int window)
    {
        if (window < 1 || ageDays is not { } age || age < window + 1) return null;

        var first = today.AddDays(-window);
        var inWindow = days.Where(d => d.Date >= first && d.Date < today).ToList();
        decimal fees = inWindow.Sum(d => d.FeesUsd), rewards = inWindow.Sum(d => d.RewardsUsd);

        var from = first.ToDateTime(TimeOnly.MinValue);
        var to = today.ToDateTime(TimeOnly.MinValue);
        var sampled = values.Where(v => v.Timestamp >= from && v.Timestamp < to).Select(v => v.ValueUsd).ToList();
        var capital = sampled.Count > 0 ? sampled.Average() : currentValueUsd;
        if (capital <= 0) return null;

        decimal Annualise(decimal earned) => earned / capital / window * 365 * 100;
        return new TrailingApr(window, Annualise(fees + rewards), Annualise(fees), fees + rewards, capital);
    }

    /// <summary>
    /// The lifetime rate restated per day spent in range: a position out of range half the time earns about twice as much
    /// while it is in range as its headline rate suggests, which separates the quality of the range from market luck.
    /// </summary>
    public static decimal? InRangeApr(decimal? lifetimeAprPercent, TimeInRange? timeInRange, double? ageDays)
    {
        if (lifetimeAprPercent is not { } apr || timeInRange is null) return null;
        if (timeInRange.PercentLow < MinTimeInRangePercent) return null;
        if (ageDays is not > 0 || timeInRange.TrackedHours < ageDays * 24 * MinCoverage) return null;
        return apr / (decimal)(timeInRange.Percent / 100);
    }

    /// <summary>
    /// How long, at <paramref name="dailyEarningsUsd"/>, until the position (or portfolio) is no longer behind simply
    /// holding. Assumes prices stay where they are: price moves change the gap in both directions.
    /// </summary>
    public static HodlBreakEven BreakEven(decimal gapUsd, decimal dailyEarningsUsd, string rateBasis)
    {
        double? days = gapUsd >= 0 ? 0 : dailyEarningsUsd > 0 ? (double)(-gapUsd / dailyEarningsUsd) : null;
        return new HodlBreakEven(gapUsd, dailyEarningsUsd, rateBasis, days);
    }

    /// <summary>
    /// Compares a position with its pool. A staked position earns the pool's emissions instead of its swap fees, so it is
    /// compared with the reward rate; an unstaked one with the fee rate. Null when either side is missing.
    /// </summary>
    public static PoolComparison? ComparePool(
        bool isStaked, PoolRates? pool, TrailingApr? apr7d, TrailingApr? apr30d, decimal? lifetimeAprPercent)
    {
        if (pool is null) return null;

        // A staked position earns no swap fees (the gauge takes them), so the pool's fee rate says nothing about it; without
        // a real emission rate there is nothing to compare with. Sources report 0 for "unknown" as often as for "none".
        if (isStaked && pool.RewardAprPercent is not > 0) return null;

        (decimal? poolApr, string poolBasis, string kind) = isStaked
            ? (pool.RewardAprPercent, "now", "rewards")
            : (pool.FeeAprPercent, pool.FeeBasis ?? "30d", "fees");
        if (poolApr is not { } p || p <= 0) return null;

        // Same window as the pool's figure where the position is old enough, otherwise its lifetime rate.
        (decimal? positionApr, string positionBasis) = (kind == "fees" ? poolBasis : "30d") switch
        {
            "30d" when apr30d is not null => (apr30d.TotalAprPercent, "30d"),
            "7d" or "24h" when apr7d is not null => (apr7d.TotalAprPercent, "7d"),
            _ => (lifetimeAprPercent, "lifetime"),
        };
        if (positionApr is not { } own) return null;

        return new PoolComparison(p, poolBasis, own, positionBasis, kind, (double)(own / p));
    }
}

/// <param name="FeeAprPercent">Pool swap-fee APR; null when the source has no volume data.</param>
/// <param name="FeeBasis">"30d", "7d" or "24h".</param>
/// <param name="RewardAprPercent">Spot emission APR, when the pool pays one.</param>
public sealed record PoolRates(decimal? FeeAprPercent, string? FeeBasis, decimal? RewardAprPercent);
