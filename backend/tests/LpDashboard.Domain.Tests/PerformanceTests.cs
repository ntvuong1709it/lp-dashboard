using LpDashboard.Domain.Metrics;

namespace LpDashboard.Domain.Tests;

public class PerformanceTests
{
    private static readonly DateOnly Today = new(2026, 11, 6);

    private static DailyEarnings Day(int daysAgo, decimal fees, decimal rewards = 0) =>
        new(Today.AddDays(-daysAgo), fees, rewards, false);

    private static ValueSample Value(int daysAgo, decimal usd) =>
        new(Today.AddDays(-daysAgo).ToDateTime(new TimeOnly(12, 0)), usd);

    // ---- trailing APR ----

    [Fact]
    public void Trailing_annualises_earnings_over_average_capital_in_the_window()
    {
        // $1/day of fees and $2/day of rewards on $1,000 over 7 days.
        var days = Enumerable.Range(1, 7).Select(d => Day(d, 1, 2)).ToList();
        var values = new[] { Value(3, 900), Value(2, 1100) };

        var t = PerformanceCalculator.Trailing(days, values, 5000, ageDays: 40, Today, 7)!;

        Assert.Equal(7, t.Days);
        Assert.Equal(21, t.EarnedUsd);
        Assert.Equal(1000, t.AverageCapitalUsd);
        Assert.Equal(3m / 1000 * 365 * 100, t.TotalAprPercent);
        Assert.Equal(1m / 1000 * 365 * 100, t.SwapFeeAprPercent);
    }

    [Fact]
    public void Trailing_ignores_today_and_days_outside_the_window()
    {
        var days = new[] { Day(0, 100), Day(1, 1), Day(7, 1), Day(8, 1000) };

        var t = PerformanceCalculator.Trailing(days, [], 1000, 40, Today, 7)!;

        Assert.Equal(2, t.EarnedUsd); // day 1 and day 7 only
    }

    [Fact]
    public void Trailing_falls_back_to_current_value_without_snapshots_in_the_window()
    {
        var t = PerformanceCalculator.Trailing([Day(1, 10)], [Value(20, 99999)], 2000, 40, Today, 7)!;

        Assert.Equal(2000, t.AverageCapitalUsd);
    }

    [Fact]
    public void Trailing_needs_a_position_older_than_the_window_and_some_capital()
    {
        Assert.Null(PerformanceCalculator.Trailing([Day(1, 10)], [], 1000, ageDays: 7, Today, 7));
        Assert.Null(PerformanceCalculator.Trailing([Day(1, 10)], [], 1000, ageDays: null, Today, 7));
        Assert.Null(PerformanceCalculator.Trailing([Day(1, 10)], [], 0, ageDays: 40, Today, 7));
        Assert.NotNull(PerformanceCalculator.Trailing([Day(1, 10)], [], 1000, ageDays: 8, Today, 7));
    }

    // ---- APR per day in range ----

    private static TimeInRange Tir(double percent, double trackedHours = 1000) =>
        new(percent, trackedHours, 0, trackedHours, 0, percent, percent);

    [Fact]
    public void InRangeApr_scales_the_lifetime_rate_by_time_in_range()
    {
        Assert.Equal(60m, PerformanceCalculator.InRangeApr(30, Tir(50), ageDays: 40));
        Assert.Equal(30m, PerformanceCalculator.InRangeApr(30, Tir(100), ageDays: 40));
    }

    [Fact]
    public void InRangeApr_is_withheld_when_the_data_is_too_thin()
    {
        Assert.Null(PerformanceCalculator.InRangeApr(30, Tir(2), 40));                       // almost never in range
        // Estimated at 60% but possibly as low as 5%: the rate could be 12x what it looks like.
        Assert.Null(PerformanceCalculator.InRangeApr(30, new TimeInRange(60, 0, 1000, 1000, 100, 5, 70), 40));
        Assert.Null(PerformanceCalculator.InRangeApr(30, Tir(50, trackedHours: 100), 40));   // 100h of 960h
        Assert.Null(PerformanceCalculator.InRangeApr(null, Tir(50), 40));
        Assert.Null(PerformanceCalculator.InRangeApr(30, null, 40));
        Assert.Null(PerformanceCalculator.InRangeApr(30, Tir(50), null));
    }

    // ---- break-even against holding ----

    [Fact]
    public void BreakEven_projects_days_to_close_the_gap()
    {
        var b = PerformanceCalculator.BreakEven(-300, 10, "30d");

        Assert.Equal(30, b.DaysToBreakEven);
        Assert.Equal("30d", b.RateBasis);
    }

    [Fact]
    public void BreakEven_is_zero_when_already_ahead_and_never_when_nothing_is_earned()
    {
        Assert.Equal(0, PerformanceCalculator.BreakEven(50, 0, "30d").DaysToBreakEven);
        Assert.Null(PerformanceCalculator.BreakEven(-50, 0, "30d").DaysToBreakEven);
        Assert.Null(PerformanceCalculator.BreakEven(-50, -1, "30d").DaysToBreakEven);
    }

    // ---- pool comparison ----

    private static TrailingApr Apr(int days, decimal total) => new(days, total, 0, 0, 1000);

    [Fact]
    public void ComparePool_unstaked_uses_fee_apr_and_the_pools_own_window()
    {
        var pool = new PoolRates(20, "30d", 50);

        var c = PerformanceCalculator.ComparePool(false, pool, Apr(7, 99), Apr(30, 30), lifetimeAprPercent: 5)!;

        Assert.Equal("fees", c.IncomeKind);
        Assert.Equal(20, c.PoolAprPercent);
        Assert.Equal("30d", c.PositionBasis);
        Assert.Equal(30, c.PositionAprPercent);
        Assert.Equal(1.5, c.Ratio);
    }

    [Fact]
    public void ComparePool_staked_uses_the_reward_rate()
    {
        var pool = new PoolRates(20, "30d", 40);

        var c = PerformanceCalculator.ComparePool(true, pool, null, Apr(30, 60), 5)!;

        Assert.Equal("rewards", c.IncomeKind);
        Assert.Equal(40, c.PoolAprPercent);
        Assert.Equal(1.5, c.Ratio);
    }

    [Fact]
    public void ComparePool_uses_seven_day_rate_for_short_pool_windows_and_lifetime_for_young_positions()
    {
        var c7 = PerformanceCalculator.ComparePool(false, new PoolRates(10, "7d", null), Apr(7, 20), null, 5)!;
        Assert.Equal("7d", c7.PositionBasis);
        Assert.Equal(2, c7.Ratio);

        var young = PerformanceCalculator.ComparePool(false, new PoolRates(10, "30d", null), null, null, 5)!;
        Assert.Equal("lifetime", young.PositionBasis);
        Assert.Equal(0.5, young.Ratio);
    }

    [Fact]
    public void ComparePool_is_null_without_a_usable_pool_or_position_rate()
    {
        Assert.Null(PerformanceCalculator.ComparePool(false, null, null, null, 5));
        Assert.Null(PerformanceCalculator.ComparePool(false, new PoolRates(null, null, null), null, null, 5));
        Assert.Null(PerformanceCalculator.ComparePool(false, new PoolRates(0, "30d", null), null, null, 5));
        Assert.Null(PerformanceCalculator.ComparePool(false, new PoolRates(10, "30d", null), null, null, null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void ComparePool_staked_without_a_real_emission_rate_is_not_compared_with_the_fee_rate(int? reward)
    {
        var pool = new PoolRates(46, "24h", reward);

        Assert.Null(PerformanceCalculator.ComparePool(true, pool, null, Apr(30, 60), 5));
        // The same pool is still comparable for someone who has not staked.
        Assert.NotNull(PerformanceCalculator.ComparePool(false, pool, Apr(7, 60), null, 5));
    }

    // ---- time in range confidence ----

    private static readonly DateTime T0 = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan MaxGap = TimeSpan.FromHours(3);

    private static TickSample Sample(int hour, int tick) => new(T0.AddHours(hour), tick);

    [Fact]
    public void Measured_time_is_always_high_confidence_even_at_the_edge()
    {
        var t = TimeInRangeCalculator.Calculate([Sample(0, 1), Sample(1, 1), Sample(2, 1)], 0, 1000, MaxGap)!;

        Assert.Equal(0, t.UncertainHours);
        Assert.Equal(t.Percent, t.PercentLow);
        Assert.Equal(t.Percent, t.PercentHigh);
        Assert.Equal(RangeConfidence.High, t.Confidence);
    }

    [Fact]
    public void Estimated_time_far_from_the_edges_is_high_confidence()
    {
        var t = TimeInRangeCalculator.Calculate([], [Sample(0, 500), Sample(1, 500), Sample(2, 5000)], 0, 1000, MaxGap, MaxGap)!;

        Assert.Equal(0, t.UncertainHours);
        Assert.Equal(RangeConfidence.High, t.Confidence);
    }

    [Fact]
    public void Estimated_time_near_an_edge_widens_the_band_both_ways()
    {
        // Hour 0-1: 50 ticks inside the upper edge (called in range, uncertain). Hour 1-2: 50 ticks outside it (called out, uncertain).
        // Hour 2-3: well inside.
        var t = TimeInRangeCalculator.Calculate([], [Sample(0, 950), Sample(1, 1050), Sample(2, 500), Sample(3, 500)], 0, 1000, MaxGap, MaxGap)!;

        Assert.Equal(2, t.UncertainHours);
        Assert.Equal(2.0 / 3 * 100, t.Percent, 6);
        Assert.Equal(1.0 / 3 * 100, t.PercentLow, 6);   // the in-range uncertain hour was really out
        Assert.Equal(3.0 / 3 * 100, t.PercentHigh, 6);  // the out-of-range uncertain hour was really in
        Assert.Equal(RangeConfidence.Low, t.Confidence);
    }

    [Fact]
    public void Measured_time_overrides_the_uncertainty_of_the_estimate_it_replaces()
    {
        var measured = new[] { Sample(0, 500), Sample(2, 500) }; // hours 0-2 measured, well inside
        var estimated = new[] { Sample(0, 950), Sample(2, 950) }; // would be uncertain
        var t = TimeInRangeCalculator.Calculate(measured, estimated, 0, 1000, MaxGap, MaxGap)!;

        Assert.Equal(0, t.UncertainHours);
        Assert.Equal(100, t.Percent);
    }

    [Fact]
    public void A_small_uncertain_share_is_medium_confidence()
    {
        // 1 uncertain hour out of 20: the band is 5 points wide.
        var samples = Enumerable.Range(0, 21).Select(h => Sample(h, h == 0 ? 990 : 500)).ToList();

        var t = TimeInRangeCalculator.Calculate([], samples, 0, 1000, MaxGap, MaxGap)!;

        Assert.Equal(1, t.UncertainHours);
        Assert.Equal(RangeConfidence.Medium, t.Confidence);
    }
}
