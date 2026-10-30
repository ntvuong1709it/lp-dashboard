using System.Numerics;
using LpDashboard.Domain.Calculations;

namespace LpDashboard.Domain.Tests;

public class PositionMathTests
{
    [Fact]
    public void TickToPrice_Tick0_EqualDecimals_IsOne() =>
        Assert.Equal(1.0, PositionMath.TickToPrice(0, 18, 18), 10);

    [Fact]
    public void TickToPrice_WethUsdc_AdjustsForDecimals()
    {
        // token0 = WETH (18), token1 = USDC (6); tick -196256 is ~3000 USDC per WETH.
        var price = PositionMath.TickToPrice(-196256, 18, 6);
        Assert.InRange(price, 2990, 3010);
    }

    [Fact]
    public void SqrtPriceX96_OfQ96_IsPriceOne()
    {
        var q96 = BigInteger.One << 96;
        Assert.Equal(1.0, PositionMath.SqrtPriceX96ToPrice(q96, 18, 18), 10);
    }

    [Theory]
    [InlineData(-10, true)]
    [InlineData(-100, true)]
    [InlineData(99, true)]
    [InlineData(100, false)]
    [InlineData(-101, false)]
    public void IsInRange_LowerInclusive_UpperExclusive(int tick, bool expected) =>
        Assert.Equal(expected, PositionMath.IsInRange(tick, -100, 100));

    [Fact]
    public void Amounts_BelowRange_AllToken0()
    {
        var (a0, a1) = PositionMath.GetAmountsRaw(new BigInteger(1e18), PositionMath.TickToSqrtPrice(-200), -100, 100);
        Assert.True(a0 > 0);
        Assert.Equal(0, a1);
    }

    [Fact]
    public void Amounts_AboveRange_AllToken1_MatchesFormula()
    {
        var l = new BigInteger(1e18);
        var (a0, a1) = PositionMath.GetAmountsRaw(l, PositionMath.TickToSqrtPrice(200), -100, 100);
        var expected = 1e18 * (PositionMath.TickToSqrtPrice(100) - PositionMath.TickToSqrtPrice(-100));
        Assert.Equal(0, a0);
        Assert.Equal(expected, a1, expected * 1e-12);
    }

    [Fact]
    public void Amounts_CenteredRange_AtPriceOne_AreRoughlyEqual()
    {
        var (a0, a1) = PositionMath.GetAmountsRaw(new BigInteger(1e18), 1.0, -100, 100);
        Assert.True(a0 > 0 && a1 > 0);
        Assert.InRange(a0 / a1, 0.99, 1.01);
    }

    [Fact]
    public void GetAmounts_ZeroLiquidity_IsZero()
    {
        var (a0, a1) = PositionMath.GetAmounts(BigInteger.Zero, BigInteger.One << 96, -100, 100, 18, 6);
        Assert.Equal(0m, a0);
        Assert.Equal(0m, a1);
    }

    [Theory]
    [InlineData(-50)]
    [InlineData(0)]
    [InlineData(80)]
    [InlineData(-300)]
    [InlineData(300)]
    public void GetLiquidityRaw_RoundTripsAmounts(int tick)
    {
        const double liquidity = 1e18;
        var (a0, a1) = PositionMath.GetAmountsRaw(liquidity, PositionMath.TickToSqrtPrice(tick), -100, 100);
        Assert.Equal(liquidity, PositionMath.GetLiquidityRaw(a0, a1, -100, 100), liquidity * 1e-9);
    }

    [Fact]
    public void PriceToSqrtPrice_InvertsTickToPrice()
    {
        var price = PositionMath.TickToPrice(-196256, 18, 6);
        var tick = PositionMath.SqrtPriceToTick(PositionMath.PriceToSqrtPrice(price * 1.0000001, 18, 6));
        Assert.Equal(-196256, tick);
    }

    [Theory]
    [InlineData("1000000000000000000", 18, "1")]
    [InlineData("1000000000000000000000000000000", 30, "1")]
    [InlineData("123", 0, "123")]
    public void TokenAmount_FromRaw(string raw, int decimals, string expected) =>
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), TokenAmount.FromRaw(BigInteger.Parse(raw), decimals));
}
