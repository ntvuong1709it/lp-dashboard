using System.Numerics;
using LpLedger.Core;
using LpLedger.Core.Models;
using static LpLedger.Core.ConcentratedLiquidityMath;

namespace LpLedger.Tests;

public class MathTests
{
    [Fact]
    public void Tick0IsPriceOneForEqualDecimals()
    {
        Assert.Equal(1, PriceFromTick(0, 18, 18), 9);
        Assert.Equal(1e12, PriceFromTick(0, 18, 6), 1e12 * 1e-9);
    }

    [Fact]
    public void InRangeHoldsBothTokensOutOfRangeHoldsOne()
    {
        var l = new BigInteger(1_000_000_000_000);
        var mid = AmountsForLiquidity(l, 1, -1000, 1000, 0, 0);
        Assert.True(mid.Amount0 > 0 && mid.Amount1 > 0);
        var below = AmountsForLiquidity(l, TickToSqrtPrice(-1500), -1000, 1000, 0, 0);
        Assert.Equal(0, below.Amount1);
        Assert.True(below.Amount0 > 0);
        var above = AmountsForLiquidity(l, TickToSqrtPrice(1500), -1000, 1000, 0, 0);
        Assert.Equal(0, above.Amount0);
        Assert.True(above.Amount1 > 0);
    }

    [Fact]
    public void AmountsAreContinuousAtTheRangeEdges()
    {
        var l = new BigInteger(5_000_000_000_000);
        var atLower = AmountsForLiquidity(l, TickToSqrtPrice(-2000), -2000, 3000, 0, 0);
        var justAbove = AmountsForLiquidity(l, TickToSqrtPrice(-2000) * 1.0000001, -2000, 3000, 0, 0);
        Assert.Equal(atLower.Amount0, justAbove.Amount0, atLower.Amount0 * 1e-6);
        Assert.Equal(0, atLower.Amount1);
        var atUpper = AmountsForLiquidity(l, TickToSqrtPrice(3000), -2000, 3000, 0, 0);
        var justBelow = AmountsForLiquidity(l, TickToSqrtPrice(3000) * 0.9999999, -2000, 3000, 0, 0);
        Assert.Equal(atUpper.Amount1, justBelow.Amount1, atUpper.Amount1 * 1e-6);
        Assert.Equal(0, atUpper.Amount0);
    }

    [Fact]
    public void DecimalsScaleTheAmounts()
    {
        var l = new BigInteger(1_000_000_000_000_000);
        var a = AmountsForLiquidity(l, 1, -100, 100, 0, 0);
        var b = AmountsForLiquidity(l, 1, -100, 100, 18, 6);
        Assert.Equal(a.Amount0 / 1e18, b.Amount0, a.Amount0 / 1e18 * 1e-9);
        Assert.Equal(a.Amount1 / 1e6, b.Amount1, a.Amount1 / 1e6 * 1e-9);
    }
}

public class PnlTests
{
    private static PnlEvent Ev(EventKind k, double a0, double a1, decimal usd) => new(k, a0, a1, usd);

    [Fact]
    public void NeverTouchedPositionPnlIsValueMinusDeposit()
    {
        var r = PnlCalculator.Compute(new PnlInput([Ev(EventKind.Add, 1, 3000, 6000)], 6500, 3500, 1, 100, null));
        Assert.Equal(600, r.NetUsd);
        Assert.Equal(6000, r.DepositedUsd);
        Assert.Equal(10, r.Pct);
        Assert.Equal(6500, r.HodlUsd);
        Assert.Equal(500, r.PriceEffectUsd);
        Assert.Equal(0, r.IlUsd);
        Assert.Equal(100, r.FeesUsd);
    }

    [Fact]
    public void BreakdownAlwaysSumsToNetPnl()
    {
        var r = PnlCalculator.Compute(new PnlInput(
            [Ev(EventKind.Add, 1, 3000, 6000), Ev(EventKind.Remove, 0.1, 300, 620), Ev(EventKind.Collect, 0.1, 305, 625)],
            5800, 3500, 1, 40, 12));
        Assert.Equal(r.NetUsd, r.PriceEffectUsd + r.IlUsd + r.FeesUsd - 12);
        Assert.Equal(5800 + 625 + 40 - 6000 - 12, r.NetUsd);
    }

    [Fact]
    public void ClosedPositionRealizesAllValue()
    {
        var r = PnlCalculator.Compute(new PnlInput(
            [Ev(EventKind.Add, 1, 3000, 6000), Ev(EventKind.Remove, 1, 3000, 6300), Ev(EventKind.Collect, 1, 3050, 6350)],
            0, 3600, 1, 0, null));
        Assert.Equal(350, r.NetUsd);
        Assert.Equal(50, r.FeesUsd);
    }
}
