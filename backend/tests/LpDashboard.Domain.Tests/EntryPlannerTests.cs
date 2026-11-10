using System.Numerics;
using LpDashboard.Domain.Calculations;
using LpDashboard.Domain.Metrics;

namespace LpDashboard.Domain.Tests;

public class EntryPlannerTests
{
    // The numbers in docs/enter-pool-mock.html with its defaults: WETH $3,400 / USDC $1, pool price 3,401.02 (0.03% above the
    // reference), spacing 10, decimals ignored by the mock's snapping (so 18/18 here), 0.10% swap cost, $0.80 gas.
    private static readonly PoolPrices MockPrices = new(3400 * 1.0003, 3400, 1);

    private static EntryPlan Plan(PoolPrices prices, SnappedRange range, double h0, double h1, double cost = 0.001, double gas = 0, double? tvl = null, int d0 = 18, int d1 = 18) =>
        EntryPlanner.Plan(h0, h1, prices, range, cost, gas, tvl, d0, d1);

    private static SnappedRange MockRange(double lo = 10, double hi = 10) =>
        EntryPlanner.SnapRange(MockPrices.Pool, lo, hi, 10, 18, 18);

    private static void Close(double expected, double actual, double relative = 1e-9) =>
        Assert.Equal(expected, actual, Math.Abs(expected) * relative + 1e-12);

    [Fact]
    public void MockReference_BudgetInQuoteToken_MatchesTheMockup()
    {
        var range = MockRange();
        Assert.Equal(80270, range.TickLower);
        Assert.Equal(82280, range.TickUpper);

        var plan = Plan(MockPrices, range, 0, 5000, gas: 0.8);

        Assert.Equal(SwapSide.SellToken1, plan.Side);
        Close(2386.3354408342366, plan.SwapAmountIn);
        Close(0.7009512162214284, plan.SwapProceeds);
        Close(0.7009512162214284, plan.Amount0);
        Close(2613.6645591657634, plan.Amount1);
        Close(2.3863354408342365, plan.SwapCostUsd);
        Close(3.1863354408342364, plan.EntryCostUsd);
        Close(4996.89869431862, plan.ValueUsd);
        Close(874.3851952163567, plan.Liquidity);
    }

    [Fact]
    public void MockReference_SkewedRange_NeedsMoreOfTheBaseToken()
    {
        var range = MockRange(5, 20);
        Assert.Equal(80810, range.TickLower);
        Assert.Equal(83150, range.TickUpper);

        var plan = Plan(MockPrices, range, 0, 5000);
        Close(1.1390948226800697, plan.Amount0);
        Close(1122.0377639325616, plan.Amount1);

        var symmetric = Plan(MockPrices, MockRange(), 0, 5000);
        Assert.True(plan.Token0Share > symmetric.Token0Share);
    }

    [Fact]
    public void RoundTrip_PlannedAmounts_GiveTheSameLiquidityAndAmountsOnChain()
    {
        // Real decimals (WETH 18, USDC 6): feed the planned amounts, in raw units, through the on-chain maths and back.
        const int d0 = 18, d1 = 6;
        var prices = new PoolPrices(3401.10, 3401.10, 1);
        var range = EntryPlanner.SnapRange(prices.Pool, 8, 12, 10, d0, d1);
        var plan = Plan(prices, range, 0.4, 2200, d0: d0, d1: d1);

        double raw0 = plan.Amount0 * 1e18, raw1 = plan.Amount1 * 1e6;
        var liquidityRaw = PositionMath.GetLiquidityRaw(raw0, raw1, range.TickLower, range.TickUpper);
        Close(plan.Liquidity * Math.Pow(10, (d0 + d1) / 2.0), liquidityRaw, 1e-9);

        var sqrt = PositionMath.PriceToSqrtPrice(prices.Pool, d0, d1);
        var (back0, back1) = PositionMath.GetAmountsRaw(liquidityRaw, sqrt, range.TickLower, range.TickUpper);
        Close(raw0, back0, 1e-8);
        Close(raw1, back1, 1e-8);
    }

    [Fact]
    public void RoundTrip_AmountsOfAnExistingPosition_NeedNoSwapAndRecoverItsLiquidity()
    {
        // A position of known raw liquidity, deposited in range: its token amounts are exactly what the range needs,
        // so the planner must ask for no swap and give the liquidity back.
        const int d0 = 18, d1 = 6;
        var range = EntryPlanner.RangeFromTicks(3400, -197200, -195000, 100, d0, d1);
        var price = PositionMath.TickToPrice(-196256, d0, d1);
        var liquidity = new BigInteger(4.2e15);
        var (raw0, raw1) = PositionMath.GetAmountsRaw(liquidity, PositionMath.PriceToSqrtPrice(price, d0, d1), range.TickLower, range.TickUpper);
        Assert.True(raw0 > 0 && raw1 > 0);

        var plan = Plan(new PoolPrices(price, price, 1), range, raw0 / 1e18, raw1 / 1e6, d0: d0, d1: d1);

        Assert.Equal(SwapSide.None, plan.Side);
        Close((double)liquidity, plan.Liquidity * Math.Pow(10, (d0 + d1) / 2.0), 1e-9);
    }

    [Theory]
    [InlineData(5000, 0, 10, 10)]
    [InlineData(0, 3, 10, 10)]
    [InlineData(1000, 1, 5, 20)]
    [InlineData(100, 2, 25, 25)]
    [InlineData(5000, 0, 1, 1)]
    public void Plan_AmountsMatchTheRangeRatio(double h1, double h0, double lo, double hi)
    {
        var range = MockRange(lo, hi);
        var plan = Plan(MockPrices, range, h0, h1);
        var (x, y) = PositionMath.AmountsPerLiquidity(MockPrices.Pool, range.PriceLower, range.PriceUpper);

        Assert.True(plan.Amount0 > 0 && plan.Amount1 > 0);
        Close(y / x, plan.Amount1 / plan.Amount0, 1e-9);
    }

    [Fact]
    public void Plan_AllQuoteToken_SellsTheQuote_AllBaseToken_SellsTheBase()
    {
        var range = MockRange();
        var quote = Plan(MockPrices, range, 0, 5000);
        var baseOnly = Plan(MockPrices, range, 1.5, 0);

        Assert.Equal(SwapSide.SellToken1, quote.Side);
        Assert.Equal(SwapSide.SellToken0, baseOnly.Side);
        Assert.True(baseOnly.SwapProceeds > 0 && baseOnly.Amount0 < 1.5);
    }

    [Fact]
    public void Plan_HoldingsAlreadyInTheRightMix_NeedNoSwap()
    {
        var range = MockRange();
        var (x, y) = PositionMath.AmountsPerLiquidity(MockPrices.Pool, range.PriceLower, range.PriceUpper);
        var plan = Plan(MockPrices, range, 2 * x * 1000, 2 * y * 1000);

        Assert.Equal(SwapSide.None, plan.Side);
        Assert.Equal(0, plan.SwapAmountIn);
        Assert.Equal(0, plan.SwapCostUsd);
        Close(2000, plan.Liquidity);
    }

    [Fact]
    public void Plan_RangeAbovePrice_NeedsOnlyTheBaseToken()
    {
        // Price below the range: a position there is all token0, so everything held in token1 is swapped.
        var range = EntryPlanner.RangeFromTicks(MockPrices.Pool, 82300, 83500, 10, 18, 18);
        Assert.True(range.PriceLower > MockPrices.Pool);

        var plan = Plan(MockPrices, range, 1, 3400);

        Assert.Equal(SwapSide.SellToken1, plan.Side);
        Assert.Equal(0, plan.Amount1);
        Close(1 + 3400 * (1 - 0.001) / MockPrices.Pool, plan.Amount0);
        Assert.True(plan.Liquidity > 0);
    }

    [Fact]
    public void Plan_RangeBelowPrice_NeedsOnlyTheQuoteToken()
    {
        var range = EntryPlanner.RangeFromTicks(MockPrices.Pool, 78000, 80000, 10, 18, 18);
        Assert.True(range.PriceUpper < MockPrices.Pool);

        var plan = Plan(MockPrices, range, 1, 3400);

        Assert.Equal(SwapSide.SellToken0, plan.Side);
        Assert.Equal(0, plan.Amount0);
        Close(3400 + 1 * MockPrices.Pool * (1 - 0.001), plan.Amount1);
        Assert.True(plan.Liquidity > 0);
    }

    [Theory]
    [InlineData(0, 5000)]
    [InlineData(1.5, 0)]
    [InlineData(0.5, 1000)]
    public void Plan_ValueAfterSwap_IsValueBeforeMinusSwapCost(double h0, double h1)
    {
        // With the pool price equal to the USD reference ratio, the only value that leaves is the swap cost.
        var prices = new PoolPrices(3400, 3400, 1);
        var plan = Plan(prices, MockRange(), h0, h1, cost: 0.003, gas: 1.5);

        Close(plan.StartValueUsd - plan.SwapCostUsd, plan.ValueUsd, 1e-9);
        Close(plan.SwapCostUsd + 1.5, plan.EntryCostUsd);
    }

    [Fact]
    public void Plan_NoSwapCost_KeepsAllValue()
    {
        var prices = new PoolPrices(3400, 3400, 1);
        var plan = Plan(prices, MockRange(), 0, 5000, cost: 0);
        Close(5000, plan.ValueUsd);
    }

    [Fact]
    public void Plan_VolatilePair_ValuesBothLegsInUsd()
    {
        // WETH $3,400 / cbBTC $98,000: pool price is 0.034694 BTC per ETH. Hold all cbBTC.
        var prices = new PoolPrices(3400.0 / 98000, 3400, 98000);
        var range = EntryPlanner.SnapRange(prices.Pool, 10, 10, 10, 18, 8);
        var plan = Plan(prices, range, 0, 0.05, cost: 0, d0: 18, d1: 8);

        Close(0.05 * 98000, plan.ValueUsd, 1e-9);
        Close(plan.Amount0 * 3400 + plan.Amount1 * 98000, plan.ValueUsd);
        Assert.InRange(plan.Token0Share, 0.4, 0.6);
    }

    [Fact]
    public void Plan_SwapShareOfTvl_WarnsAboveTheThreshold()
    {
        var range = MockRange();
        var small = Plan(MockPrices, range, 0, 5000, tvl: 48_200_000);   // ~2,386 of 48.2M = 0.005%
        var large = Plan(MockPrices, range, 0, 500_000, tvl: 48_200_000); // ~238k = 0.5%
        var unknown = Plan(MockPrices, range, 0, 500_000);

        Assert.False(small.SwapShareWarning);
        Assert.True(large.SwapShareWarning);
        Assert.InRange(large.SwapShareOfTvl!.Value, 0.004, 0.006);
        Assert.Null(unknown.SwapShareOfTvl);
        Assert.False(unknown.SwapShareWarning);
    }

    [Fact]
    public void Plan_DepositAmounts_AreRoundedDownToDecimalsAndDustIsTheRemainder()
    {
        var prices = new PoolPrices(3401.10, 3401.10, 1);
        var range = EntryPlanner.SnapRange(prices.Pool, 10, 10, 10, 18, 6);
        var plan = Plan(prices, range, 0.123456789123, 777.7777777, d0: 18, d1: 6);

        Assert.True(plan.Deposit0 <= plan.Amount0 && plan.Deposit1 <= plan.Amount1);
        // USDC has 6 decimals; WETH is capped at 8 for display.
        Close(Math.Floor(plan.Amount1 * 1e6) / 1e6, plan.Deposit1);
        Close(Math.Floor(plan.Amount0 * 1e8) / 1e8, plan.Deposit0);
        Assert.InRange(plan.DustUsd, 0, 0.001);
        Close((plan.Amount0 - plan.Deposit0) * prices.Usd0 + (plan.Amount1 - plan.Deposit1) * prices.Usd1, plan.DustUsd, 1e-6);
    }

    [Fact]
    public void Plan_NothingHeld_IsAnEmptyPlan()
    {
        var plan = Plan(MockPrices, MockRange(), 0, 0);
        Assert.Equal(SwapSide.None, plan.Side);
        Assert.Equal(0, plan.ValueUsd);
        Assert.Equal(0, plan.Liquidity);
    }

    [Fact]
    public void Plan_RejectsBadInput()
    {
        var range = MockRange();
        Assert.Throws<ArgumentOutOfRangeException>(() => Plan(MockPrices, range, -1, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => Plan(MockPrices, range, 1, 5, cost: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Plan(new PoolPrices(0, 1, 1), range, 1, 5));
    }

    [Fact]
    public void HoldingsFromBudget_SplitsByTheChosenToken()
    {
        var (a0, a1) = EntryPlanner.HoldingsFromBudget(5000, PayingWith.Token1, MockPrices);
        Assert.Equal((0, 5000), (a0, a1));

        (a0, a1) = EntryPlanner.HoldingsFromBudget(5000, PayingWith.Token0, MockPrices);
        Close(5000.0 / 3400, a0);
        Assert.Equal(0, a1);

        (a0, a1) = EntryPlanner.HoldingsFromBudget(5000, PayingWith.Split, MockPrices);
        Close(2500.0 / 3400, a0);
        Assert.Equal(2500, a1);
    }

    [Theory]
    [InlineData(1, 20)]
    [InlineData(10, 10)]
    [InlineData(60, 5)]
    [InlineData(200, 25)]
    public void SnapRange_TicksAreOnTheGrid_AndTheRangeSurroundsThePrice(int spacing, double percent)
    {
        var range = EntryPlanner.SnapRange(3401.10, percent, percent, spacing, 18, 6);

        Assert.Equal(0, range.TickLower % spacing);
        Assert.Equal(0, range.TickUpper % spacing);
        Assert.True(range.TickUpper > range.TickLower);
        Assert.True(range.PriceLower < 3401.10 && range.PriceUpper > 3401.10);
    }

    [Fact]
    public void SnapRange_ReportsTheActualDistances_CloseToTheRequestedOnes()
    {
        var range = EntryPlanner.SnapRange(3401.10, 10, 10, 10, 18, 6);
        // One 10-tick step is 0.1%: snapping moves a bound by at most half of that.
        Assert.InRange(range.LowerPercent, -10.06, -9.94);
        Assert.InRange(range.UpperPercent, 9.94, 10.06);
        Close(PositionMath.TickToPrice(range.TickLower, 18, 6), range.PriceLower);
        Close(PositionMath.TickToPrice(range.TickUpper, 18, 6), range.PriceUpper);
    }

    [Fact]
    public void SnapRange_WideSpacing_ShowsALargerGapBetweenRequestedAndActual()
    {
        // Spacing 200 is a 2% step: a 0.5% request cannot be honoured, and the result says so.
        var range = EntryPlanner.SnapRange(3401.10, 0.5, 0.5, 200, 18, 6);
        Assert.True(range.TickWidth >= 200);
        Assert.True(Math.Abs(range.UpperPercent - 0.5) > 0.2 || Math.Abs(range.LowerPercent + 0.5) > 0.2);
    }

    [Fact]
    public void SnapRange_ClampsThePercentages()
    {
        var wide = EntryPlanner.SnapRange(3401.10, 500, 9000, 10, 18, 6);
        Assert.InRange(wide.LowerPercent, -99.2, -98.8);
        Assert.InRange(wide.UpperPercent, 499, 501);

        var tiny = EntryPlanner.SnapRange(3401.10, 0, 0, 1, 18, 6);
        Assert.InRange(tiny.LowerPercent, -0.12, -0.08);
        Assert.InRange(tiny.UpperPercent, 0.08, 0.12);
    }

    [Fact]
    public void SnapRange_WethUsdc_GivesTicksNearTheRealPrice()
    {
        // WETH/USDC at 3,401.10 is tick about -195,001 with 18/6 decimals; -10% and +10% are about -196,055 and -194,048.
        var range = EntryPlanner.SnapRange(3401.10, 10, 10, 10, 18, 6);
        Assert.InRange(range.TickLower, -196_065, -196_045);
        Assert.InRange(range.TickUpper, -194_058, -194_038);
    }

    [Fact]
    public void RangeFromTicks_SnapsToTheSpacing_AndRejectsAnEmptyRange()
    {
        var range = EntryPlanner.RangeFromTicks(3401.10, -197_203, -195_098, 100, 18, 6);
        Assert.Equal(-197_200, range.TickLower);
        Assert.Equal(-195_100, range.TickUpper);

        Assert.Throws<ArgumentException>(() => EntryPlanner.RangeFromTicks(3401.10, -195_000, -195_040, 100, 18, 6));
        Assert.Throws<ArgumentException>(() => EntryPlanner.RangeFromTicks(3401.10, -195_000, -196_000, 100, 18, 6));
    }

    [Fact]
    public void SnapTick_RoundsToTheNearestSpacing_AndStaysInBounds()
    {
        Assert.Equal(-196_260, PositionMath.SnapTick(-196_256.4, 10));
        Assert.Equal(-196_300, PositionMath.SnapTick(-196_256.4, 100));
        Assert.Equal(887_270, PositionMath.SnapTick(9_999_999, 10));
        Assert.Equal(-887_200, PositionMath.SnapTick(-9_999_999, 200));
        Assert.Throws<ArgumentOutOfRangeException>(() => PositionMath.SnapTick(5, 0));
    }

    [Fact]
    public void AmountsPerLiquidity_ClampsOutsideTheRange_AndMatchesGetAmountsRaw()
    {
        double pa = 2000, pb = 4000;
        var (x, y) = PositionMath.AmountsPerLiquidity(1000, pa, pb);
        Assert.True(x > 0);
        Assert.Equal(0, y);
        (x, y) = PositionMath.AmountsPerLiquidity(5000, pa, pb);
        Assert.Equal(0, x);
        Assert.True(y > 0);

        // Same thing through the tick-based function (equal decimals so the price needs no adjusting).
        int tl = 76_000, tu = 83_000;
        double lo = PositionMath.TickToPrice(tl, 18, 18), hi = PositionMath.TickToPrice(tu, 18, 18), mid = PositionMath.TickToPrice(79_000, 18, 18);
        var (hx, hy) = PositionMath.AmountsPerLiquidity(mid, lo, hi);
        var (rx, ry) = PositionMath.GetAmountsRaw(1.0, PositionMath.TickToSqrtPrice(79_000), tl, tu);
        Close(rx, hx);
        Close(ry, hy);
    }
}
