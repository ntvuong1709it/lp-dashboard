using LpDashboard.Domain.Calculations;
using LpDashboard.Domain.Entities;

namespace LpDashboard.Domain.Tests;

public class PositionHistoryTests
{
    // WETH (18) / USDC (6) around 3000 USDC per WETH.
    private const int TickLower = -197000;
    private const int TickUpper = -195500;
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static PositionEvent Event(PositionEventType type, int hours, decimal a0, decimal a1, decimal p0 = 3000m, decimal p1 = 1m) => new()
    {
        Type = type,
        Timestamp = T0.AddHours(hours),
        BlockNumber = hours,
        Amount0 = a0,
        Amount1 = a1,
        Price0Usd = p0,
        Price1Usd = p1
    };

    private static (decimal A0, decimal A1) AmountsAt(double liquidity, decimal price0)
    {
        var sqrt = PositionMath.PriceToSqrtPrice((double)price0, 18, 6);
        var (raw0, raw1) = PositionMath.GetAmountsRaw(liquidity, sqrt, TickLower, TickUpper);
        return ((decimal)(raw0 / 1e18), (decimal)(raw1 / 1e6));
    }

    [Fact]
    public void Build_ValuesDepositAtEventPrices_AndRevaluesSamples()
    {
        var (a0, a1) = AmountsAt(1e15, 3000m);
        var events = new List<PositionEvent> { Event(PositionEventType.IncreaseLiquidity, 0, a0, a1) };

        var points = PositionHistory.Build(events, TickLower, TickUpper, 18, 6, [new PriceSample(T0.AddHours(1), 3100m, 1m)]);

        Assert.Equal(2, points.Count);
        Assert.Equal((double)(a0 * 3000m + a1), (double)points[0].ValueUsd, 0.01);
        var (b0, b1) = AmountsAt(1e15, 3100m);
        Assert.Equal((double)(b0 * 3100m + b1), (double)points[1].ValueUsd, 0.01);
        Assert.Equal((double)(a0 * 3100m + a1), (double)points[1].HodlValueUsd, 0.01);
    }

    [Fact]
    public void Build_StopsAfterFullWithdrawal_AndCountsClaimedFees()
    {
        var (a0, a1) = AmountsAt(1e15, 3000m);
        var (w0, w1) = AmountsAt(1e15, 2900m);
        var events = new List<PositionEvent>
        {
            Event(PositionEventType.IncreaseLiquidity, 0, a0, a1),
            new() { Type = PositionEventType.Collect, Timestamp = T0.AddHours(2), BlockNumber = 2, Fee0 = 0.01m, Fee1 = 5m, Price0Usd = 2950m, Price1Usd = 1m },
            Event(PositionEventType.DecreaseLiquidity, 4, w0, w1, 2900m)
        };
        var samples = new[] { new PriceSample(T0.AddHours(3), 2950m, 1m), new PriceSample(T0.AddHours(5), 2800m, 1m) };

        var points = PositionHistory.Build(events, TickLower, TickUpper, 18, 6, samples);

        Assert.Equal([T0, T0.AddHours(3), T0.AddHours(4)], points.Select(p => p.Timestamp));
        Assert.Equal((double)(w0 * 2900m + w1), (double)points[2].ValueUsd, 0.01);
        Assert.Equal(34.5, (double)(points[1].ValueWithFeesUsd - points[1].ValueUsd), 1e-9);
    }
}
