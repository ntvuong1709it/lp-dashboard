using LpDashboard.Domain.Metrics;

namespace LpDashboard.Domain.Tests;

public class TimeInRangeTests
{
    private static readonly DateTime T0 = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan MaxGap = TimeSpan.FromHours(3);

    private static TickSample Sample(int hour, int tick) => new(T0.AddHours(hour), tick);

    [Fact]
    public void EachIntervalCountsAsTheStateOfItsEarlierSample()
    {
        // in, in, out, out -> intervals: in (1h), in (1h), out (1h)
        var result = TimeInRangeCalculator.Calculate(
            [Sample(0, 50), Sample(1, 60), Sample(2, 150), Sample(3, 160)], 0, 100, MaxGap);

        Assert.NotNull(result);
        Assert.Equal(200.0 / 3, result.Percent, 6);
        Assert.Equal(3, result.TrackedHours);
        Assert.Equal(3, result.SpanHours);
    }

    [Fact]
    public void UpperTick_IsOutOfRange()
    {
        var result = TimeInRangeCalculator.Calculate([Sample(0, 100), Sample(1, 100)], 0, 100, MaxGap);

        Assert.Equal(0, result!.Percent);
    }

    [Fact]
    public void FewerThanTwoSamples_IsNull()
    {
        Assert.Null(TimeInRangeCalculator.Calculate([], 0, 100, MaxGap));
        Assert.Null(TimeInRangeCalculator.Calculate([Sample(0, 50)], 0, 100, MaxGap));
    }

    [Fact]
    public void GapsLongerThanMaxGap_AreExcludedNotGuessed()
    {
        // 1h in range, then a 10h hole (app was off), then 1h out of range.
        var result = TimeInRangeCalculator.Calculate(
            [Sample(0, 50), Sample(1, 50), Sample(11, 150), Sample(12, 150)], 0, 100, MaxGap);

        Assert.Equal(2, result!.TrackedHours);
        Assert.Equal(12, result.SpanHours);
        Assert.Equal(50, result.Percent);
    }

    [Fact]
    public void OnlyGaps_IsNull()
    {
        Assert.Null(TimeInRangeCalculator.Calculate([Sample(0, 50), Sample(10, 50)], 0, 100, MaxGap));
    }

    [Fact]
    public void UnorderedSamples_AreSortedFirst()
    {
        var result = TimeInRangeCalculator.Calculate([Sample(2, 50), Sample(0, 50), Sample(1, 150)], 0, 100, MaxGap);

        // 0h out... order: 0:50(in) 1:150(out) 2:50 -> in 1h, out 1h
        Assert.Equal(50, result!.Percent);
    }
}

public class TimeInRangeEstimateTests
{
    private static readonly DateTime T0 = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan MaxGap = TimeSpan.FromHours(3);
    private static readonly TimeSpan MaxEstimatedGap = TimeSpan.FromHours(30);

    private static TickSample S(double hour, int tick) => new(T0.AddHours(hour), tick);

    private static TimeInRange? Calc(TickSample[] measured, TickSample[] estimated) =>
        TimeInRangeCalculator.Calculate(measured, estimated, 0, 100, MaxGap, MaxEstimatedGap);

    [Fact]
    public void EstimatedOnly_CoversPositionsWithoutSnapshots()
    {
        // 24h in range, then 8h out of range (samples every 8h).
        var result = Calc([], [S(0, 50), S(8, 50), S(16, 50), S(24, 150), S(32, 150)]);

        Assert.NotNull(result);
        Assert.Equal(0, result.MeasuredHours);
        Assert.Equal(32, result.EstimatedHours);
        Assert.Equal(75, result.Percent);
    }

    [Fact]
    public void MeasuredIntervals_OverrideTheEstimateWhereTheyOverlap()
    {
        // Estimate says out of range all day; snapshots measured 2h in range inside that day.
        var result = Calc([S(10, 50), S(11, 50), S(12, 50)], [S(0, 150), S(24, 150)]);

        Assert.Equal(2, result!.MeasuredHours);
        Assert.Equal(22, result.EstimatedHours);
        Assert.Equal(2.0 / 24 * 100, result.Percent, 6);
    }

    [Fact]
    public void GapsInSnapshots_AreFilledFromTheEstimate()
    {
        // Snapshots at 0-1h and 11-12h (10h hole); the estimate covers the hole as out of range.
        var result = Calc([S(0, 50), S(1, 50), S(11, 50), S(12, 50)], [S(1, 150), S(6, 150), S(11, 150)]);

        Assert.Equal(2, result!.MeasuredHours);
        Assert.Equal(10, result.EstimatedHours);
        Assert.Equal(2.0 / 12 * 100, result.Percent, 6);
    }

    [Fact]
    public void EstimatedGapsLongerThanAllowed_AreNotGuessed()
    {
        // Liquidity was withdrawn between hour 8 and hour 100: no estimated samples in between.
        var result = Calc([], [S(0, 50), S(8, 50), S(100, 50), S(108, 50)]);

        Assert.Equal(16, result!.EstimatedHours);
    }

    [Fact]
    public void NothingCovered_IsNull()
    {
        Assert.Null(Calc([S(0, 50)], [S(0, 50)]));
    }
}

public class PortfolioHistoryTests
{
    private static readonly DateTime T0 = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private static ValuePoint P(double hour, decimal value, decimal unclaimed = 0) => new(T0.AddHours(hour), value, unclaimed);

    [Fact]
    public void SumsPositionsAndCarriesLastValueForward()
    {
        var a = new PositionSeries([P(0, 100), P(2, 120)], null);
        var b = new PositionSeries([P(1, 50, 5)], null);

        var result = PortfolioHistoryCalculator.Build([a, b], null, null);

        Assert.Collection(result,
            p => { Assert.Equal(T0, p.Timestamp); Assert.Equal(100, p.ValueUsd); Assert.Equal(1, p.PositionCount); },
            p => { Assert.Equal(150, p.ValueUsd); Assert.Equal(5, p.UnclaimedUsd); Assert.Equal(2, p.PositionCount); },
            p => { Assert.Equal(170, p.ValueUsd); Assert.Equal(2, p.PositionCount); });
    }

    [Fact]
    public void ClosedPosition_StopsContributingAtCloseTime()
    {
        var closed = new PositionSeries([P(0, 100), P(1, 100)], T0.AddHours(2));
        var open = new PositionSeries([P(0, 10), P(4, 10)], null);

        var result = PortfolioHistoryCalculator.Build([closed, open], null, null);

        Assert.Equal(110, result.Single(p => p.Timestamp == T0.AddHours(1)).ValueUsd);
        Assert.Equal(10, result.Single(p => p.Timestamp == T0.AddHours(2)).ValueUsd);
        Assert.Equal(1, result.Single(p => p.Timestamp == T0.AddHours(4)).PositionCount);
    }

    [Fact]
    public void DownsamplesToAtMostRoughlyMaxPoints()
    {
        var points = Enumerable.Range(0, 1000).Select(h => P(h, h)).ToList();

        var result = PortfolioHistoryCalculator.Build([new PositionSeries(points, null)], null, null, maxPoints: 100);

        Assert.InRange(result.Count, 2, 101);
        Assert.Equal(T0.AddHours(999), result[^1].Timestamp);
    }

    [Fact]
    public void FromAndTo_LimitTheRange()
    {
        var series = new PositionSeries(Enumerable.Range(0, 10).Select(h => P(h, 1)).ToList(), null);

        var result = PortfolioHistoryCalculator.Build([series], T0.AddHours(3), T0.AddHours(6));

        Assert.Equal([T0.AddHours(3), T0.AddHours(4), T0.AddHours(5)], result.Select(p => p.Timestamp));
    }

    [Fact]
    public void NoSnapshots_IsEmpty()
    {
        Assert.Empty(PortfolioHistoryCalculator.Build([new PositionSeries([], null)], null, null));
    }
}
