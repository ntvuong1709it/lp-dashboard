using LpDashboard.Application.Pools;
using LpDashboard.Domain;
using LpDashboard.Domain.Metrics;
using LpDashboard.Infrastructure.Pools;

namespace LpDashboard.Domain.Tests;

public class AprEstimatorTests
{
    [Fact]
    public void Concentration_NarrowerRange_IsHigher()
    {
        Assert.True(AprEstimator.Concentration(0.01) > AprEstimator.Concentration(0.10));
        Assert.True(AprEstimator.Concentration(0.10) > AprEstimator.Concentration(0.50));
    }

    [Fact]
    public void Concentration_Plus50Percent_MatchesV3Formula()
    {
        // ratio Pl/Pu = 0.5/1.5, so C = 1 / (1 - (1/3)^0.25) = 4.163
        Assert.Equal(4.163, AprEstimator.Concentration(0.5), 2);
    }

    [Fact]
    public void TimeInRange_NoVolatility_IsAlwaysInRange() =>
        Assert.Equal(1.0, AprEstimator.TimeInRange(0.05, 0, 30));

    [Fact]
    public void TimeInRange_WiderRange_StaysInLonger()
    {
        var narrow = AprEstimator.TimeInRange(0.05, 0.6, 30);
        var wide = AprEstimator.TimeInRange(0.30, 0.6, 30);
        Assert.InRange(narrow, 0, 1);
        Assert.True(wide > narrow);
    }

    [Fact]
    public void TimeInRange_LongerHorizon_IsLower() =>
        Assert.True(AprEstimator.TimeInRange(0.10, 0.6, 90) < AprEstimator.TimeInRange(0.10, 0.6, 7));

    [Fact]
    public void TimeInRange_KnownValue_OneSigmaRange()
    {
        // Day 1 only: range of +/- ~1 daily sigma holds ~68% of outcomes.
        var annual = 0.5;
        var daily = annual / Math.Sqrt(365);
        var w = Math.Exp(daily) - 1; // upper edge at +1 sigma; lower edge at ln(1-w) is a little below -1 sigma
        var t = AprEstimator.TimeInRange(w, annual, 1);
        Assert.InRange(t, 0.66, 0.72);
    }

    [Fact]
    public void HalfWidthFromTicks_SymmetricAroundZero_MatchesPriceRatio()
    {
        // ticks +/-953 are about +/-10% in price: 1.0001^953 = 1.1.
        Assert.Equal(0.0953, AprEstimator.HalfWidthFromTicks(-953, 953), 3);
    }

    [Fact]
    public void HalfWidthFromTicks_WiderRange_IsLarger() =>
        Assert.True(AprEstimator.HalfWidthFromTicks(-5000, 5000) > AprEstimator.HalfWidthFromTicks(-500, 500));

    [Fact]
    public void ImpermanentLoss_NoMove_IsZero() =>
        Assert.Equal(0, AprEstimator.ImpermanentLoss(0.1, 0), 10);

    [Fact]
    public void ImpermanentLoss_GrowsWithMove_AndWithNarrowerRange()
    {
        Assert.True(AprEstimator.ImpermanentLoss(0.1, 0.05) > AprEstimator.ImpermanentLoss(0.1, 0.02));
        Assert.True(AprEstimator.ImpermanentLoss(0.05, 0.04) > AprEstimator.ImpermanentLoss(0.5, 0.04));
    }

    [Fact]
    public void ImpermanentLoss_FullyOutOfRange_Saturates_OnTheWayUp()
    {
        // Above the range the position is all token1 and the loss against holding keeps growing with the price.
        Assert.True(AprEstimator.ImpermanentLoss(0.1, 0.5) > AprEstimator.ImpermanentLoss(0.1, 0.2));
    }

    [Fact]
    public void ImpermanentLoss_SmallMove_IsConstantProductLossTimesConcentration()
    {
        // Full-range (v2) loss for a +5% move is 1 - 2*sqrt(1.05)/2.05 = 0.0297%; a concentrated range multiplies it by about C.
        var v2 = 1 - 2 * Math.Sqrt(1.05) / 2.05;
        Assert.Equal(v2 * AprEstimator.Concentration(0.1), AprEstimator.ImpermanentLoss(0.1, 0.05), 3);
    }

    [Fact]
    public void BreakEven_MoreFees_AllowsABiggerMove()
    {
        var small = AprEstimator.BreakEvenMove(0.1, 0.005, up: false)!.Value;
        var large = AprEstimator.BreakEvenMove(0.1, 0.02, up: false)!.Value;
        Assert.True(large > small);
    }

    [Fact]
    public void BreakEven_LossEqualsFeesAtTheReturnedMove()
    {
        var m = AprEstimator.BreakEvenMove(0.1, 0.01, up: true)!.Value;
        Assert.Equal(0.01, AprEstimator.ImpermanentLoss(0.1, m), 4);
    }

    [Fact]
    public void BreakEven_NoFees_IsZero() =>
        Assert.Equal(0, AprEstimator.BreakEvenMove(0.1, 0, up: true));

    [Fact]
    public void Backtest_PriceStaysInside_NeverExits()
    {
        var h = AprEstimator.Backtest([100, 101, 99, 102, 98], 0.05);
        Assert.Equal(4, h.DaysInRange);
        Assert.Equal(4, h.Days);
        Assert.Null(h.FirstExitDay);
        Assert.Equal(0.02, h.HighMove, 10);
        Assert.Equal(-0.02, h.LowMove, 10);
    }

    [Fact]
    public void Backtest_CountsDaysOutsideAndFirstExit()
    {
        // +/-5% around 100 is 95..105: days 3 and 4 close outside.
        var h = AprEstimator.Backtest([100, 102, 110, 111, 103], 0.05);
        Assert.Equal(2, h.DaysInRange);
        Assert.Equal(4, h.Days);
        Assert.Equal(2, h.FirstExitDay);
    }

    [Fact]
    public void Volatility_ConstantGrowth_IsZero() =>
        Assert.Equal(0, AprEstimator.AnnualisedVolatility([100, 101, 102.01, 103.0301, 104.060401]), 10);

    [Fact]
    public void Volatility_AlternatingMoves_IsPositiveAndAnnualised()
    {
        // Returns alternate +ln(1.1) and -ln(1.1): sample stdev ~0.1 per day.
        var v = AprEstimator.AnnualisedVolatility([100, 110, 100, 110, 100, 110]);
        Assert.InRange(v, 1.5, 2.1); // 0.1 * sqrt(365) = 1.91
    }

    [Fact]
    public void Volatility_TooFewPoints_IsZero() =>
        Assert.Equal(0, AprEstimator.AnnualisedVolatility([100, 101]));

    [Fact]
    public void Estimate_ReferenceRange_ReturnsPoolApr()
    {
        var vol = 0.6;
        var reference = AprEstimator.ReferenceHalfWidth(vol);
        var e = AprEstimator.Estimate(40, 30, 50, reference, vol, 30, 1000, 10_000_000);
        Assert.Equal(1.0, e.Scale, 6);
        Assert.Equal(40, e.Apr, 6);
        Assert.Equal(30, e.AprLow, 6);
        Assert.Equal(50, e.AprHigh, 6);
    }

    [Fact]
    public void Estimate_NarrowerThanReference_EarnsMoreWhileInRange()
    {
        var vol = 0.6;
        var e = AprEstimator.Estimate(40, 40, 40, 0.05, vol, 30, 1000, 10_000_000);
        Assert.True(e.Apr > 40);
        Assert.True(e.TimeInRange < 1);
    }

    [Fact]
    public void Estimate_BandAlwaysContainsApr()
    {
        var e = AprEstimator.Estimate(40, 45, 60, 0.1, 0.6, 30, 1000, 10_000_000);
        Assert.True(e.AprLow <= e.Apr && e.Apr <= e.AprHigh);
    }

    [Fact]
    public void Estimate_LargeDepositInSmallPool_HasLargeShare()
    {
        var small = AprEstimator.Estimate(40, 40, 40, 0.1, 0.6, 30, 500_000, 1_000_000);
        var tiny = AprEstimator.Estimate(40, 40, 40, 0.1, 0.6, 30, 500, 1_000_000);
        Assert.True(small.DepositShare > 0.05);
        Assert.True(tiny.DepositShare < 0.05);
    }

    [Theory]
    [InlineData("0.3%", 0.3, null)]
    [InlineData("0.05%", 0.05, null)]
    [InlineData("CL100 - 0.3%", 0.3, 100)]
    [InlineData("CL1 - 0.0085%", 0.0085, 1)]
    public void ParseMeta_ReadsFeeAndTickSpacing(string meta, double fee, int? spacing)
    {
        var (f, s) = DefiLlamaPoolStatsSource.ParseMeta(meta);
        Assert.Equal((decimal)fee, f);
        Assert.Equal(spacing, s);
    }

    [Fact]
    public void ParseMeta_Unknown_IsNull() => Assert.Equal((null, null), DefiLlamaPoolStatsSource.ParseMeta("weird"));

    [Fact]
    public void PoolStats_Aprs_UseVolumeFeeAndTvl()
    {
        // 1M volume/day at 0.3% on 10M TVL: 3,000 fees/day -> 10.95% a year.
        var p = new PoolStats("id", Chain.Base, Protocol.UniswapV3, "A-B", "0xa", "0xb", 0.3m, null, 10_000_000m, 1_000_000m, 7_000_000m, null, null);
        Assert.Equal(10.95m, p.FeeApr7d!.Value, 2);
        Assert.Equal(1_000_000m, p.Volume7dAvgUsd);
        Assert.Equal(3000, p.FeeHundredthsBip);
    }

    [Fact]
    public void PoolStats_WithFee_RecomputesBothAprs()
    {
        // The source says 0.025% (6.26% APR on its own numbers); the pool really charges 0.0885%.
        var p = new PoolStats("id", Chain.Base, Protocol.AerodromeSlipstream, "A-B", "0xa", "0xb", 0.025m, 50, 2_419_736m, 1_660_143m, 11_621_001m, 6.26m, null);
        var fixedFee = p.WithFee(0.0885m);
        Assert.Equal(0.0885m, fixedFee.FeePercent);
        Assert.Equal(p.FeeApr7d!.Value * 3.54m, fixedFee.FeeApr7d!.Value, 1);
        Assert.Equal(22.16m, fixedFee.FeeApr1d!.Value, 1);
    }

    [Fact]
    public void MatchKey_IgnoresTokenOrderAndCase()
    {
        var a = PoolExplorerService.MatchKey(Protocol.UniswapV3, "0xAA", "0xbb", 500, 10);
        var b = PoolExplorerService.MatchKey(Protocol.UniswapV3, "0xBB", "0xaa", 500, 60);
        Assert.Equal(a, b);
    }

    [Fact]
    public void MatchKey_Slipstream_UsesTickSpacingNotFee()
    {
        var a = PoolExplorerService.MatchKey(Protocol.AerodromeSlipstream, "0xaa", "0xbb", 3000, 100);
        var b = PoolExplorerService.MatchKey(Protocol.AerodromeSlipstream, "0xaa", "0xbb", null, 100);
        var c = PoolExplorerService.MatchKey(Protocol.AerodromeSlipstream, "0xaa", "0xbb", 3000, 200);
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }
}
