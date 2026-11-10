using LpDashboard.Domain.Calculations;
using LpDashboard.Domain.Metrics;

namespace LpDashboard.Domain.Tests;

public class HedgePlannerTests
{
    // Same inputs as the mockup defaults (see EntryPlannerTests): WETH $3,400, pool price 3,401.02, +/-10%, $5,000 of USDC.
    private static readonly PoolPrices Prices = new(3400 * 1.0003, 3400, 1);

    private static (EntryPlan Entry, SnappedRange Range) Enter(double lo = 10, double hi = 10, double budget = 5000, double gas = 0.8)
    {
        var range = EntryPlanner.SnapRange(Prices.Pool, lo, hi, 10, 18, 18);
        var (h0, h1) = EntryPlanner.HoldingsFromBudget(budget, PayingWith.Token1, Prices);
        return (EntryPlanner.Plan(h0, h1, Prices, range, 0.001, gas), range);
    }

    private static HedgePlan Hedge(double ratio = 1, HedgeInstrument instrument = HedgeInstrument.PerpShort, double rate = 0.08,
        double leverage = 3, double income = 0.22, double lo = 10, double hi = 10, double fee = 0.0005)
    {
        var (entry, range) = Enter(lo, hi);
        return HedgePlanner.Plan(entry, Prices, range, HedgedLegs.Token0, new HedgeSettings(instrument, ratio, rate, income, leverage, fee));
    }

    private static HedgeScenario At(HedgePlan plan, double move) => plan.Scenarios.Single(s => s.MovePct == move);

    private static void Close(double expected, double actual, double relative = 1e-9) =>
        Assert.Equal(expected, actual, Math.Abs(expected) * relative + 1e-9);

    [Fact]
    public void MockReference_PerpShort_MatchesTheMockup()
    {
        var plan = Hedge();
        var leg = Assert.Single(plan.Shorts);

        Close(0.7009512162214284, leg.Amount);
        Close(2383.2341351528567, plan.NotionalUsd);
        Close(0.5223526871567905, plan.CarryPerDayUsd);        // funding is earned: the mock shows it as -0.52 of cost
        Close(-13.287346479550859, plan.HedgeCostUsd);
        Close(794.4113783842855, plan.MarginUsd);
        Close(5791.310072702906, plan.CapitalUsedUsd);
        Close(90.35488050000792, plan.FeesUsd);
        Close(0.21773664549492458, plan.HedgedNetApr);
        Close(4517.688233333333, plan.LiquidationPoolPrice!.Value);
        Assert.False(plan.LiquidatesBeforeRangeTop);

        Close(1.5109633026098013, leg.AtLowerBound);
        Assert.Equal(0, leg.AtUpperBound);
        Close(0.38950209798382773, leg.RehedgeIfDown);
        Close(-0.36133824489845234, leg.RehedgeIfUp);
    }

    [Fact]
    public void MockReference_Scenarios_MatchTheMockup()
    {
        var plan = Hedge();
        Assert.Equal(31, plan.Scenarios.Count);

        var m10 = At(plan, -10);
        Close(-372.67889426137936, m10.PriceEffectUsd);
        Close(238.39491053934015, m10.HedgePnlUsd);
        Close(29.96529553426579, m10.DaysInRange);
        Close(-285.61487313068005, m10.NoHedgeNetUsd);
        Close(-33.932616111789045, m10.HedgedNetUsd);

        Close(-1373.5450598000998, At(plan, -30).NoHedgeNetUsd);
        Close(-645.0729817025283, At(plan, -30).HedgedNetUsd);
        Close(87.16854505917368, At(plan, 0).NoHedgeNetUsd);
        Close(100.45589153872454, At(plan, 0).HedgedNetUsd);
        Close(-304.3711250427359, At(plan, 20).HedgedNetUsd);
        Close(143.9980108323512, At(plan, 30).NoHedgeNetUsd);
        Close(10.049267050300138, At(plan, 30).DaysInRange);
    }

    [Fact]
    public void MockReference_SkewedRange_WithHalfHedgeBorrowing()
    {
        var plan = Hedge(ratio: 0.5, instrument: HedgeInstrument.BorrowAndSell, rate: 0.05, lo: 5, hi: 20);

        Close(0.5695474113400348, plan.Shorts[0].Amount);
        Close(-0.2652686573364546, plan.CarryPerDayUsd);       // borrow interest is paid
        Close(9.894520918649757, plan.HedgeCostUsd);
        Equal0(plan.MarginUsd);
        Close(0.19589903943930725, plan.HedgedNetApr);
        Close(0.7387838056368035, plan.Shorts[0].AtLowerBound);
        Close(59.01595543158317, At(plan, 20).HedgedNetUsd);
        Close(426.37068886220203, At(plan, 30).NoHedgeNetUsd);
        Close(20.051857838696552, At(plan, 30).DaysInRange);
    }

    private static void Equal0(double v) => Assert.Equal(0, v);

    [Fact]
    public void ZeroRatio_IsExactlyNoHedge()
    {
        var plan = Hedge(ratio: 0);

        Assert.Equal(0, plan.NotionalUsd);
        Assert.Equal(0, plan.HedgeCostUsd);
        Assert.Equal(0, plan.MarginUsd);
        Assert.All(plan.Scenarios, s =>
        {
            Assert.Equal(0, s.HedgePnlUsd);
            Assert.Equal(s.NoHedgeNetUsd, s.HedgedNetUsd, 10);
        });
        Close(plan.UnhedgedApr, plan.HedgedNetApr);
        Assert.Null(plan.LiquidationMovePct);
        Assert.False(plan.LiquidatesBeforeRangeTop);
    }

    [Fact]
    public void FullHedge_IsNearlyFlatForSmallMoves_ComparedWithNoHedge()
    {
        var plan = Hedge();
        foreach (var move in new[] { -2.0, 2.0 })
        {
            var dHedged = Math.Abs(At(plan, move).HedgedNetUsd - At(plan, 0).HedgedNetUsd);
            var dNone = Math.Abs(At(plan, move).NoHedgeNetUsd - At(plan, 0).NoHedgeNetUsd);
            Assert.True(dHedged < 0.2 * dNone, $"{move}%: hedged moves {dHedged:F2}, unhedged {dNone:F2}");
        }
    }

    [Fact]
    public void Hedge_ProfitsWhenThePriceFalls_AndLosesWhenItRises()
    {
        var plan = Hedge();
        Assert.True(At(plan, -10).HedgePnlUsd > 0);
        Assert.True(At(plan, 10).HedgePnlUsd < 0);
        Assert.Equal(0, At(plan, 0).HedgePnlUsd);
    }

    [Fact]
    public void PerpFundingSign_PositiveIsEarned_NegativeIsPaid_AndBorrowIsTheOpposite()
    {
        var earn = Hedge(rate: 0.10);
        var pay = Hedge(rate: -0.10);
        var borrow = Hedge(instrument: HedgeInstrument.BorrowAndSell, rate: 0.10);

        Assert.True(earn.CarryPerDayUsd > 0);
        Assert.True(pay.CarryPerDayUsd < 0);
        Assert.True(borrow.CarryPerDayUsd < 0);
        Close(earn.NotionalUsd * 0.10 / 365, earn.CarryPerDayUsd);
        Close(-earn.CarryPerDayUsd, pay.CarryPerDayUsd);
        Close(-earn.CarryPerDayUsd, borrow.CarryPerDayUsd);
        Assert.True(earn.HedgeCostUsd < pay.HedgeCostUsd);
    }

    [Fact]
    public void TradingFees_AreAlwaysACost_EvenWhenFundingIsEarned()
    {
        var free = Hedge(rate: 0.10, fee: 0);
        var charged = Hedge(rate: 0.10, fee: 0.0005);

        Close(charged.NotionalUsd * 0.0005 * 2, charged.TradingFeesUsd);
        Close(charged.HedgeCostUsd - free.HedgeCostUsd, charged.TradingFeesUsd);
    }

    [Fact]
    public void BorrowHedge_NetAprDropsByTheRateTimesNotionalOverValue()
    {
        // No margin and no trading fee: the only change from the unhedged fee APR is interest on the borrowed notional.
        var (entry, range) = Enter();
        var plan = HedgePlanner.Plan(entry, Prices, range, HedgedLegs.Token0,
            new HedgeSettings(HedgeInstrument.BorrowAndSell, 1, 0.05, 0.22, TradingFee: 0));

        Close(0.22 - 0.05 * plan.NotionalUsd / entry.ValueUsd, plan.HedgedNetApr);
        Equal0(plan.MarginUsd);
    }

    [Fact]
    public void PerpHedge_ApyIsMeasuredOnValuePlusMargin()
    {
        var plan = Hedge(rate: 0, fee: 0, leverage: 4);
        Close(plan.NotionalUsd / 4, plan.MarginUsd);
        Close(0.22 * (5000 - 3.186) / (5000 - 3.186 + plan.MarginUsd), plan.HedgedNetApr, 1e-3);
        Assert.True(plan.HedgedNetApr < plan.UnhedgedApr);
    }

    [Fact]
    public void Liquidation_HighLeverageFailsBeforeTheTopOfTheRange()
    {
        var safe = Hedge(leverage: 3);
        var risky = Hedge(leverage: 20);

        Close(100 * (1.0 / 3 - 0.005), safe.LiquidationMovePct!.Value);
        Assert.False(safe.LiquidatesBeforeRangeTop);
        Close(100 * (1.0 / 20 - 0.005), risky.LiquidationMovePct!.Value);
        Assert.True(risky.LiquidatesBeforeRangeTop);
        Close(Prices.Pool * (1 + 1.0 / 20 - 0.005), risky.LiquidationPoolPrice!.Value);
        Assert.True(risky.UpperBoundMovePct > risky.LiquidationMovePct!.Value);
    }

    [Fact]
    public void BorrowAndSell_HasNoLiquidationNumbers()
    {
        var plan = Hedge(instrument: HedgeInstrument.BorrowAndSell, leverage: 20);
        Assert.Null(plan.LiquidationMovePct);
        Assert.Null(plan.LiquidationPoolPrice);
        Assert.False(plan.LiquidatesBeforeRangeTop);
        Assert.Null(plan.Shorts[0].LiquidationPriceUsd);
    }

    [Fact]
    public void EdgeShorts_ShrinkAsThePriceRises_ToNothingAtTheTop()
    {
        var leg = Hedge().Shorts[0];
        Assert.True(leg.AtLowerBound > leg.Amount);
        Assert.Equal(0, leg.AtUpperBound);
        Assert.True(leg.RehedgeIfDown > 0);   // price down: hold more token0, short more
        Assert.True(leg.RehedgeIfUp < 0);     // price up: cut the short
        Assert.Equal(0, leg.NotionalAtUpperBoundUsd);
        Assert.True(leg.NotionalAtLowerBoundUsd > leg.NotionalUsd);
    }

    [Fact]
    public void EdgeShorts_ScaleWithTheRatio()
    {
        var full = Hedge(ratio: 1).Shorts[0];
        var half = Hedge(ratio: 0.5).Shorts[0];
        Close(full.AtLowerBound / 2, half.AtLowerBound);
        Close(full.RehedgeIfDown / 2, half.RehedgeIfDown);
    }

    [Fact]
    public void Scenarios_FeesStopWhenThePriceLeavesTheRange()
    {
        var plan = Hedge();
        Assert.Equal(30, At(plan, 0).DaysInRange, 9);
        Assert.Equal(30, At(plan, -2).DaysInRange, 9);
        Assert.True(At(plan, -30).DaysInRange < 12);
        Assert.True(At(plan, 30).DaysInRange < 12);
        Assert.True(At(plan, -30).FeesUsd < At(plan, -10).FeesUsd);
        Close(plan.FeesUsd * At(plan, -30).DaysInRange / 30, At(plan, -30).FeesUsd);
    }

    [Fact]
    public void Scenarios_PriceEffectIsFlatAboveTheRangeTop_AndFallsBelowTheBottom()
    {
        var plan = Hedge();
        Close(At(plan, 20).PriceEffectUsd, At(plan, 30).PriceEffectUsd, 1e-6);
        Assert.True(At(plan, -30).PriceEffectUsd < At(plan, -20).PriceEffectUsd);
        Assert.Equal(0, At(plan, 0).PriceEffectUsd, 9);
    }

    [Fact]
    public void Scenarios_ReportThePoolPriceAndTheOutcomeRange()
    {
        var plan = Hedge();
        Close(Prices.Pool * 0.9, At(plan, -10).PoolPrice);
        Assert.Equal(plan.Scenarios.Min(s => s.NoHedgeNetUsd), plan.NoHedgeMinUsd);
        Assert.Equal(plan.Scenarios.Max(s => s.HedgedNetUsd), plan.HedgedMaxUsd);
        Assert.True(plan.HedgedMinUsd > plan.NoHedgeMinUsd);   // the hedge cuts the downside
    }

    [Fact]
    public void RangeEdges_AreReportedAsMovesOfTheVolatileToken()
    {
        var (_, range) = Enter();
        var plan = Hedge();
        Close((range.PriceLower / Prices.Pool - 1) * 100, plan.LowerBoundMovePct);
        Close((range.PriceUpper / Prices.Pool - 1) * 100, plan.UpperBoundMovePct);
    }

    [Fact]
    public void Scenarios_StepIsConfigurable()
    {
        var (entry, range) = Enter();
        var plan = HedgePlanner.Plan(entry, Prices, range, HedgedLegs.Token0, new HedgeSettings(HedgeInstrument.PerpShort, 1, 0.08, 0.22), scenarioStepPct: 5);
        Assert.Equal(13, plan.Scenarios.Count);
        Assert.Equal(-30, plan.Scenarios[0].MovePct);
        Assert.Equal(30, plan.Scenarios[^1].MovePct);
    }

    [Fact]
    public void QuoteVolatileMirror_GivesTheSameNumbersAsTheBaseVolatilePool()
    {
        // The same market seen the other way round: USDC is token0 and WETH token1, price = USDC-per-... inverted.
        var (entry, range) = Enter();
        var plan = HedgePlanner.Plan(entry, Prices, range, HedgedLegs.Token0, new HedgeSettings(HedgeInstrument.PerpShort, 0.8, 0.08, 0.22));

        var mirrorPrices = new PoolPrices(1 / Prices.Pool, 1, 3400);
        var mirrorRange = new SnappedRange(-range.TickUpper, -range.TickLower, 1 / range.PriceUpper, 1 / range.PriceLower, 0, 0);
        var (m0, m1) = EntryPlanner.HoldingsFromBudget(5000, PayingWith.Token0, mirrorPrices);
        var mirrorEntry = EntryPlanner.Plan(m0, m1, mirrorPrices, mirrorRange, 0.001, 0.8);
        var mirror = HedgePlanner.Plan(mirrorEntry, mirrorPrices, mirrorRange, HedgedLegs.Token1, new HedgeSettings(HedgeInstrument.PerpShort, 0.8, 0.08, 0.22));

        Close(entry.Liquidity, mirrorEntry.Liquidity, 1e-9);
        Assert.Equal(1, mirror.Shorts[0].Token);
        Close(plan.Shorts[0].Amount, mirror.Shorts[0].Amount);
        Close(plan.NotionalUsd, mirror.NotionalUsd);
        Close(plan.HedgedNetApr, mirror.HedgedNetApr);
        Close(plan.LowerBoundMovePct, mirror.LowerBoundMovePct, 1e-8);
        Close(plan.UpperBoundMovePct, mirror.UpperBoundMovePct, 1e-8);
        // The lower bound of one pool is the upper bound of the other: the volatile token is all in the position there.
        Close(plan.Shorts[0].AtLowerBound, mirror.Shorts[0].AtUpperBound, 1e-8);
        Assert.Equal(0, mirror.Shorts[0].AtLowerBound);
        Close(plan.Shorts[0].RehedgeIfDown, mirror.Shorts[0].RehedgeIfDown, 1e-8);
        Close(plan.Shorts[0].RehedgeIfUp, mirror.Shorts[0].RehedgeIfUp, 1e-8);
        Assert.Equal(plan.Scenarios.Count, mirror.Scenarios.Count);
        for (var i = 0; i < plan.Scenarios.Count; i++)
        {
            Close(plan.Scenarios[i].NoHedgeNetUsd, mirror.Scenarios[i].NoHedgeNetUsd, 1e-7);
            Close(plan.Scenarios[i].HedgedNetUsd, mirror.Scenarios[i].HedgedNetUsd, 1e-7);
            Close(plan.Scenarios[i].DaysInRange, mirror.Scenarios[i].DaysInRange, 1e-7);
        }
    }

    [Fact]
    public void VolatilePair_HasOneShortPerLeg_AndNoScenarios()
    {
        var prices = new PoolPrices(3400.0 / 98000, 3400, 98000);
        var range = EntryPlanner.SnapRange(prices.Pool, 10, 10, 10, 18, 8);
        var entry = EntryPlanner.Plan(0, 0.05, prices, range, 0.001, 1, null, 18, 8);

        var plan = HedgePlanner.Plan(entry, prices, range, HedgedLegs.Both, new HedgeSettings(HedgeInstrument.PerpShort, 1, 0.08, 0.17));

        Assert.Equal(2, plan.Shorts.Count);
        Assert.Equal([0, 1], plan.Shorts.Select(s => s.Token));
        Close(entry.Amount0, plan.Shorts[0].Amount);
        Close(entry.Amount1, plan.Shorts[1].Amount);
        Close(entry.Amount0 * 3400 + entry.Amount1 * 98000, plan.NotionalUsd);
        Close(plan.NotionalUsd / 3, plan.MarginUsd);
        Assert.Empty(plan.Scenarios);
        Assert.False(plan.LiquidatesBeforeRangeTop);
        // Each leg's own USD liquidation price.
        Close(3400 * (1 + 1.0 / 3 - 0.005), plan.Shorts[0].LiquidationPriceUsd!.Value);
        Close(98000 * (1 + 1.0 / 3 - 0.005), plan.Shorts[1].LiquidationPriceUsd!.Value);
        // At the lower bound the position is all token0, at the upper all token1.
        Assert.Equal(0, plan.Shorts[1].AtLowerBound);
        Assert.Equal(0, plan.Shorts[0].AtUpperBound);
    }

    [Fact]
    public void StablePair_HasNothingToHedge()
    {
        var prices = new PoolPrices(1.0001, 1, 1);
        var range = EntryPlanner.SnapRange(prices.Pool, 0.5, 0.5, 1, 6, 6);
        var entry = EntryPlanner.Plan(0, 1000, prices, range, 0.0001);

        var plan = HedgePlanner.Plan(entry, prices, range, HedgedLegs.None, new HedgeSettings(HedgeInstrument.PerpShort, 1, 0.08, 0.05));

        Assert.Empty(plan.Shorts);
        Assert.Empty(plan.Scenarios);
        Assert.Equal(0, plan.NotionalUsd);
        Assert.Equal(0, plan.HedgeCostUsd);
    }

    [Fact]
    public void PriceOutsideTheRange_ShortsAreZeroOrFull()
    {
        // Range above the price: all token0, so the short is the whole token0 amount, with no scenario drift in value below.
        var range = EntryPlanner.RangeFromTicks(Prices.Pool, 82300, 83500, 10, 18, 18);
        var entry = EntryPlanner.Plan(1, 3400, Prices, range, 0.001);
        var plan = HedgePlanner.Plan(entry, Prices, range, HedgedLegs.Token0, new HedgeSettings(HedgeInstrument.PerpShort, 1, 0.08, 0.22));

        Close(entry.Amount0, plan.Shorts[0].Amount);
        Assert.Equal(0, entry.Amount1);
        Assert.Equal(0, At(plan, 0).DaysInRange);   // today's price is outside the range: no fees until it gets in
    }

    [Fact]
    public void ClassifyLegs_UsesTheAssetClassifier()
    {
        Assert.Equal(HedgedLegs.Token0, HedgePlanner.ClassifyLegs("WETH", "USDC"));
        Assert.Equal(HedgedLegs.Token1, HedgePlanner.ClassifyLegs("USDC", "cbBTC"));
        Assert.Equal(HedgedLegs.Both, HedgePlanner.ClassifyLegs("WETH", "cbBTC"));
        Assert.Equal(HedgedLegs.None, HedgePlanner.ClassifyLegs("USDC", "USDT"));
        Assert.Equal(HedgedLegs.Token0, HedgePlanner.ClassifyLegs("AERO", "USDC"));   // unknown tokens are treated as volatile
    }

    [Theory]
    [InlineData(100, 90, 80, 120, 1)]          // the whole path is inside
    [InlineData(100, 70, 80, 120, 2.0 / 3)]    // 100 -> 80 inside, 80 -> 70 outside
    [InlineData(100, 130, 80, 120, 2.0 / 3)]
    [InlineData(100, 100, 80, 120, 1)]
    [InlineData(130, 100, 80, 120, 2.0 / 3)]   // starts outside, enters the range
    [InlineData(130, 140, 80, 120, 0)]
    public void InRangeShare_IsTheShareOfTheStraightPathInside(double from, double to, double lo, double hi, double expected) =>
        Assert.Equal(expected, HedgePlanner.InRangeShare(from, to, lo, hi), 12);

    [Fact]
    public void InRangeShare_ZeroMove_IsOneInsideAndZeroOutside()
    {
        Assert.Equal(1, HedgePlanner.InRangeShare(100, 100, 80, 120));
        Assert.Equal(0, HedgePlanner.InRangeShare(150, 150, 80, 120));
    }
}
