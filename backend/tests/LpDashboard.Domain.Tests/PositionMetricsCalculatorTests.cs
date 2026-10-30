using LpDashboard.Domain.Entities;
using LpDashboard.Domain.Metrics;

namespace LpDashboard.Domain.Tests;

public class PositionMetricsCalculatorTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void OpenPosition_NoPriceMove_PnlEqualsFees()
    {
        var events = new List<PositionEvent> { Deposit(T0, 1m, 2000m, 2000m) };
        var current = new PositionValuation(1m, 2000m, 0.01m, 0m, 0m, 2000m, 1m, null);

        var m = PositionMetricsCalculator.Calculate(events, current, T0, null, T0.AddDays(7));

        Assert.Equal(4000m, m.DepositedUsd);
        Assert.Equal(4000m, m.CurrentValueUsd);
        Assert.Equal(20m, m.FeesEarnedUsd);
        Assert.Equal(20m, m.PnlUsd);
        Assert.Equal(4000m, m.HodlValueUsd);
        Assert.Equal(0m, m.ImpermanentLossUsd);
        Assert.Equal(20m, m.PnlVsHodlUsd);
        Assert.Equal(7, m.AgeDays!.Value, 6);
    }

    [Fact]
    public void OpenPosition_PriceUp_ShowsImpermanentLoss()
    {
        var events = new List<PositionEvent> { Deposit(T0, 1m, 2000m, 2000m) };
        var current = new PositionValuation(0.8m, 2400m, 0m, 0m, 0m, 2500m, 1m, null);

        var m = PositionMetricsCalculator.Calculate(events, current, T0, null, T0.AddDays(1));

        Assert.Equal(4400m, m.CurrentValueUsd);
        Assert.Equal(400m, m.PnlUsd);
        Assert.Equal(4500m, m.HodlValueUsd);
        Assert.Equal(-100m, m.ImpermanentLossUsd);
        Assert.Equal(-100m, m.PnlVsHodlUsd);
    }

    [Fact]
    public void ClosedPosition_UsesEventPricesAndRealizedValues()
    {
        var close = T0.AddDays(10);
        var decrease = Ev(PositionEventType.DecreaseLiquidity, close, 1, 0.8m, 2400m, 2500m);
        var collect = Ev(PositionEventType.Collect, close, 2, 0.85m, 2450m, 2500m);
        var events = new List<PositionEvent> { Deposit(T0, 1m, 2000m, 2000m), decrease, collect };
        CollectSplitter.Apply(events);

        var m = PositionMetricsCalculator.Calculate(events, null, T0, close, close.AddDays(5));

        Assert.Equal(4400m, m.WithdrawnUsd);
        Assert.Equal(175m, m.CollectedFeesUsd);
        Assert.Equal(575m, m.PnlUsd);
        Assert.Equal(4500m, m.HodlValueUsd);
        Assert.Equal(-100m, m.ImpermanentLossUsd);
        Assert.Equal(75m, m.PnlVsHodlUsd);
        Assert.Equal(10, m.AgeDays!.Value, 6);
    }

    [Fact]
    public void Gas_IsSubtractedFromPnl()
    {
        var deposit = Deposit(T0, 1m, 2000m, 2000m);
        deposit.GasNative = 0.001m;
        deposit.GasUsd = 2m;
        var current = new PositionValuation(1m, 2000m, 0m, 0m, 0m, 2000m, 1m, null);

        var m = PositionMetricsCalculator.Calculate([deposit], current, T0, null, T0.AddDays(1));

        Assert.Equal(2m, m.GasUsd);
        Assert.Equal(-2m, m.PnlUsd);
    }

    [Fact]
    public void MissingPrice_IsFlagged()
    {
        var deposit = Deposit(T0, 1m, 2000m, 2000m);
        deposit.Price0Usd = null;

        var m = PositionMetricsCalculator.Calculate([deposit], null, T0, T0, T0);

        Assert.True(m.HasMissingPrices);
    }

    [Fact]
    public void FeeApr_IsAnnualizedOnAverageCapital()
    {
        var events = new List<PositionEvent> { Deposit(T0, 0m, 1000m, 2000m) };
        var current = new PositionValuation(0m, 1000m, 0m, 10m, 0m, 2000m, 1m, null);

        var m = PositionMetricsCalculator.Calculate(events, current, T0, null, T0.AddDays(10));

        Assert.Equal(36.5m, Math.Round(m.FeeAprPercent!.Value, 4));
    }

    [Fact]
    public void Rewards_CountAsFees()
    {
        var claim = Ev(PositionEventType.RewardClaim, T0.AddDays(1), 0, 0m, 0m, 2000m);
        claim.RewardAmount = 10m;
        claim.RewardPriceUsd = 0.5m;
        var current = new PositionValuation(1m, 2000m, 0m, 0m, 4m, 2000m, 1m, 0.5m);

        var m = PositionMetricsCalculator.Calculate([Deposit(T0, 1m, 2000m, 2000m), claim], current, T0, null, T0.AddDays(2));

        Assert.Equal(5m, m.ClaimedRewardsUsd);
        Assert.Equal(2m, m.PendingRewardsUsd);
        Assert.Equal(7m, m.FeesEarnedUsd);
    }

    private static PositionEvent Deposit(DateTime at, decimal a0, decimal a1, decimal price0)
    {
        var e = Ev(PositionEventType.IncreaseLiquidity, at, 0, a0, a1, price0);
        e.Principal0 = a0;
        e.Principal1 = a1;
        return e;
    }

    private static PositionEvent Ev(PositionEventType type, DateTime at, int logIndex, decimal a0, decimal a1, decimal price0) => new()
    {
        Type = type,
        Timestamp = at,
        BlockNumber = (long)(at - T0).TotalHours + 1,
        LogIndex = logIndex,
        TxHash = $"0x{at.Ticks:x}",
        Amount0 = a0,
        Amount1 = a1,
        Price0Usd = price0,
        Price1Usd = 1m
    };
}
