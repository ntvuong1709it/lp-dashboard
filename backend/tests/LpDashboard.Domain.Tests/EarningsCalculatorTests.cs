using LpDashboard.Domain.Entities;
using LpDashboard.Domain.Metrics;

namespace LpDashboard.Domain.Tests;

public class EarningsCalculatorTests
{
    private static readonly DateTime Day1 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void UnclaimedFees_AreAttributedToTheDayTheyAccrued()
    {
        var points = new[]
        {
            Point(Day1, 0),
            Point(Day1.AddHours(12), 5),
            Point(Day1.AddHours(36), 8)
        };

        var days = EarningsCalculator.Calculate([], points, TimeZoneInfo.Utc);

        Assert.Equal(2, days.Count);
        Assert.Equal(6.5m, days[0].FeesUsd);
        Assert.Equal(1.5m, days[1].FeesUsd);
    }

    [Fact]
    public void Claim_DoesNotCountAsNewEarnings()
    {
        var claim = CollectSplitterTests.Ev(PositionEventType.Collect, 0, 0, 5m, 0);
        claim.Timestamp = Day1.AddHours(13);
        claim.Fee0 = 5m;

        var points = new[]
        {
            Point(Day1, 0),
            Point(Day1.AddHours(12), 5),
            Point(Day1.AddHours(14), 1)
        };

        var days = EarningsCalculator.Calculate([claim], points, TimeZoneInfo.Utc);

        Assert.Equal(6m, days.Sum(d => d.FeesUsd));
    }

    [Fact]
    public void LongGap_IsSpreadAcrossDaysProportionally()
    {
        var points = new[] { Point(Day1.AddHours(12), 0), Point(Day1.AddHours(60), 48) };

        var days = EarningsCalculator.Calculate([], points, TimeZoneInfo.Utc);

        Assert.Equal(3, days.Count);
        Assert.Equal(12m, days[0].FeesUsd);
        Assert.Equal(24m, days[1].FeesUsd);
        Assert.Equal(12m, days[2].FeesUsd);
    }

    [Fact]
    public void RewardsAndPriceChanges_UseAmountDeltasAtLatestPrice()
    {
        var points = new[]
        {
            new EarningsPoint(Day1, 0, 0, 10m, null, null, 1m),
            new EarningsPoint(Day1.AddHours(6), 0, 0, 14m, null, null, 2m)
        };

        var days = EarningsCalculator.Calculate([], points, TimeZoneInfo.Utc);

        Assert.Equal(8m, Assert.Single(days).RewardsUsd);
    }

    [Fact]
    public void ClaimsBeforeTracking_AreSpreadOverTheDaysSincePreviousClaim()
    {
        var open = CollectSplitterTests.Ev(PositionEventType.IncreaseLiquidity, 0, 0, 1m, 1m);
        open.Timestamp = Day1;
        var claim = CollectSplitterTests.Ev(PositionEventType.Collect, 1, 0, 0, 0);
        claim.Timestamp = Day1.AddDays(4);
        claim.Fee0 = 8m;
        claim.Price0Usd = 1m;
        claim.Price1Usd = 1m;

        var days = EarningsCalculator.Calculate([open, claim], [Point(Day1.AddDays(5), 0)], TimeZoneInfo.Utc);

        Assert.Equal(new[] { 2m, 2m, 2m, 2m }, days.Where(d => d.FeesUsd > 0).Select(d => d.FeesUsd));
    }

    [Fact]
    public void MissingPrice_IsFlagged()
    {
        var points = new[] { Point(Day1, 0, price0: null), Point(Day1.AddHours(1), 1, price0: null) };

        var day = Assert.Single(EarningsCalculator.Calculate([], points, TimeZoneInfo.Utc));

        Assert.True(day.HasMissingPrices);
    }

    [Fact]
    public void TokenAmounts_AreReportedPerDayAlongsideUsd()
    {
        var points = new[]
        {
            new EarningsPoint(Day1, 0, 0, 0, 2m, 3m, 0.5m),
            new EarningsPoint(Day1.AddHours(6), 4m, 1m, 10m, 2m, 3m, 0.5m)
        };

        var day = Assert.Single(EarningsCalculator.Calculate([], points, TimeZoneInfo.Utc));

        Assert.Equal(4m, day.Fee0);
        Assert.Equal(1m, day.Fee1);
        Assert.Equal(10m, day.RewardAmount);
        Assert.Equal(8m, day.Fee0Usd);
        Assert.Equal(3m, day.Fee1Usd);
        Assert.Equal(11m, day.FeesUsd);
        Assert.Equal(5m, day.RewardsUsd);
    }

    [Fact]
    public void TokenAmounts_AreSpreadAcrossDaysLikeUsd()
    {
        var points = new[]
        {
            new EarningsPoint(Day1.AddHours(12), 0, 0, 0, 1m, 1m, 1m),
            new EarningsPoint(Day1.AddHours(60), 48m, 0, 24m, 1m, 1m, 1m)
        };

        var days = EarningsCalculator.Calculate([], points, TimeZoneInfo.Utc);

        Assert.Equal(new[] { 12m, 24m, 12m }, days.Select(d => d.Fee0));
        Assert.Equal(new[] { 6m, 12m, 6m }, days.Select(d => d.RewardAmount));
    }

    [Fact]
    public void BackfilledClaims_KeepTheirTokenAmounts()
    {
        var open = CollectSplitterTests.Ev(PositionEventType.IncreaseLiquidity, 0, 0, 1m, 1m);
        open.Timestamp = Day1;
        var claim = CollectSplitterTests.Ev(PositionEventType.Collect, 1, 0, 0, 0);
        claim.Timestamp = Day1.AddDays(4);
        claim.Fee0 = 8m;
        claim.Price0Usd = 2m;

        var days = EarningsCalculator.Calculate([open, claim], [Point(Day1.AddDays(5), 0)], TimeZoneInfo.Utc);

        Assert.Equal(8m, days.Sum(d => d.Fee0));
        Assert.Equal(16m, days.Sum(d => d.Fee0Usd));
    }

    [Fact]
    public void MissingPrice_StillReportsTheTokenAmount()
    {
        var points = new[] { Point(Day1, 0, price0: null), Point(Day1.AddHours(1), 1, price0: null) };

        var day = Assert.Single(EarningsCalculator.Calculate([], points, TimeZoneInfo.Utc));

        Assert.Equal(1m, day.Fee0);
        Assert.Equal(0m, day.Fee0Usd);
    }

    private static EarningsPoint Point(DateTime at, decimal uncollected0, decimal? price0 = 1m) =>
        new(at, uncollected0, 0, 0, price0, 1m, null);
}
