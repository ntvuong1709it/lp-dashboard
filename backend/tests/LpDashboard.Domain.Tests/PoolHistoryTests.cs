using System.Numerics;
using System.Text.Json;
using LpDashboard.Application.Pools;
using LpDashboard.Infrastructure.Pools;

namespace LpDashboard.Domain.Tests;

public class PoolHistoryTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static List<PoolChartPoint> Chart(int days, decimal tvl = 1_000_000m, decimal? apr = 36.5m) =>
        Enumerable.Range(0, days).Select(i => new PoolChartPoint(Start.AddDays(i), tvl, apr, null)).ToList();

    [Fact]
    public void Build_DerivesFeesAndVolumeFromTvlAndApr()
    {
        // 1M TVL at 36.5% APR earns 1,000 a day in fees; at a 0.3% fee that is about 333,333 of volume.
        var s = PoolHistory.Build("p", Chart(30), 0.3m, null);

        Assert.Equal(30, s.Points.Count);
        Assert.Equal(1_000m, s.Points[0].FeesUsd!.Value, 3);
        Assert.Equal(333_333.333m, s.Points[0].VolumeUsd!.Value, 2);
        Assert.Equal(7_000m, s.Fees7dUsd!.Value, 3);
        Assert.Equal(30_000m, s.Fees30dUsd!.Value, 3);
        Assert.Equal(36.5m, s.Apr30d);
        Assert.Equal(0.3m, s.FeePercent);
    }

    [Fact]
    public void Build_OtherFee_KeepsVolumeAndRescalesFeesAndApr()
    {
        var s = PoolHistory.Build("p", Chart(30), 0.3m, 0.05m);

        Assert.Equal(333_333.333m, s.Points[0].VolumeUsd!.Value, 2);
        Assert.Equal(1_000m / 6, s.Points[0].FeesUsd!.Value, 3);
        Assert.Equal(36.5m / 6, s.Points[0].FeeApr!.Value, 3);
        Assert.Equal(0.05m, s.FeePercent);
    }

    [Fact]
    public void Build_KeepsOnlyTheLast30Days()
    {
        var s = PoolHistory.Build("p", Chart(60), 0.3m, null);

        Assert.Equal(30, s.Points.Count);
        Assert.Equal(DateOnly.FromDateTime(Start.AddDays(30)), s.Points[0].Date);
    }

    [Fact]
    public void Build_NoFeeTier_HasNoFeesOrVolume()
    {
        var s = PoolHistory.Build("p", Chart(30), null, null);

        Assert.All(s.Points, p => Assert.Null(p.VolumeUsd));
        Assert.Null(s.Fees30dUsd);
        Assert.Null(s.Volume30dUsd);
    }

    [Fact]
    public void Build_TooLittleHistory_HasNoTotalsOrAverage()
    {
        var s = PoolHistory.Build("p", Chart(5), 0.3m, null);

        Assert.Equal(5, s.Points.Count);
        Assert.Null(s.Apr30d);
        Assert.Null(s.Fees7dUsd);
        Assert.Null(s.Fees30dUsd);
    }

    [Fact]
    public void MeanFeeApr_SkipsDaysWithoutARate()
    {
        var chart = Chart(12, apr: 10m);
        chart.Add(new PoolChartPoint(Start.AddDays(12), 1m, null, null));
        chart.Add(new PoolChartPoint(Start.AddDays(13), 1m, 20m, null));

        Assert.Equal((10m * 12 + 20m) / 13, PoolHistory.MeanFeeApr(chart));
    }

    [Fact]
    public void MeanFeeApr_FewerThanTenDays_IsNull() => Assert.Null(PoolHistory.MeanFeeApr(Chart(9)));

    [Fact]
    public void ParseChart_ReadsDailyPointsOldestFirst()
    {
        using var doc = JsonDocument.Parse("""
            {"status":"success","data":[
              {"timestamp":"2026-01-02T23:01:11.585Z","tvlUsd":2000.5,"apy":9,"apyBase":4.5,"apyReward":1.5},
              {"timestamp":"2026-01-01T23:01:11.585Z","tvlUsd":1000,"apyBase":null,"apyReward":null},
              {"timestamp":"2026-01-03T23:01:11.585Z","apyBase":3},
              {"tvlUsd":5}
            ]}
            """);

        var points = DefiLlamaPoolStatsSource.ParseChart(doc.RootElement);

        Assert.Equal(2, points.Count);
        Assert.Equal(1000m, points[0].TvlUsd);
        Assert.Null(points[0].FeeApr);
        Assert.Equal(new DateTime(2026, 1, 2, 23, 1, 11, 585, DateTimeKind.Utc), points[1].Date);
        Assert.Equal(4.5m, points[1].FeeApr);
        Assert.Equal(1.5m, points[1].RewardApr);
    }

    [Fact]
    public void ParseChart_NoData_IsEmpty()
    {
        using var doc = JsonDocument.Parse("""{"status":"error"}""");
        Assert.Empty(DefiLlamaPoolStatsSource.ParseChart(doc.RootElement));
    }

    [Fact]
    public void WithTvl_ReplacesTvlAndScalesCurrentAprsInversely()
    {
        var p = new PoolStats("id", Chain.Base, Protocol.AerodromeSlipstream, "A-B", "0xa", "0xb", 0.0375m, 50, 2_500_000m,
            3_200_000m, 7_000_000m, 17.5m, 30m, FeeApr30d: 20m);

        var t = p.WithTvl(7_500_000m);

        Assert.Equal(7_500_000m, t.TvlUsd);
        Assert.Equal(2_500_000m, t.SourceTvlUsd);
        Assert.Equal(17.5m / 3, t.FeeApr1d!.Value, 6);
        // The 30-day mean was recorded at the TVLs the source got wrong, so it is dropped rather than rescaled.
        Assert.Null(t.FeeApr30d);
        Assert.Equal(10m, t.RewardApr!.Value, 6);
        // The 7-day rate is worked out from volume and TVL, so it follows the new TVL by itself.
        Assert.Equal(p.FeeApr7d!.Value / 3, t.FeeApr7d!.Value, 6);
    }

    [Fact]
    public void WithTvl_AppliedAgain_KeepsTheSourceTvl()
    {
        var p = new PoolStats("id", Chain.Base, Protocol.UniswapV3, "A-B", "0xa", "0xb", 0.3m, null, 1_000_000m, 1m, 7m, 10m, null);

        var t = p.WithTvl(2_000_000m).WithTvl(4_000_000m);

        Assert.Equal(1_000_000m, t.SourceTvlUsd);
        Assert.Equal(4_000_000m, t.TvlUsd);
        Assert.Equal(2.5m, t.FeeApr1d);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void WithTvl_NonPositive_IsIgnored(int tvl)
    {
        var p = new PoolStats("id", Chain.Base, Protocol.UniswapV3, "A-B", "0xa", "0xb", 0.3m, null, 1_000_000m, 1m, 7m, 10m, null);

        Assert.Same(p, p.WithTvl(tvl));
        Assert.Null(p.SourceTvlUsd);
    }

    [Fact]
    public void TvlFromBalances_ValuesBothTokensAtTodaysPrices()
    {
        // 2,965.5 WETH (18 decimals) at $2,224.39 plus 1,904,709 USDC (6 decimals) at $1.
        var tvl = PoolExplorerService.TvlFromBalances(
            BigInteger.Parse("2965512802572342500000"), 18, 2224.39m, new BigInteger(1_904_709_418_029), 6, 1m);

        Assert.InRange(tvl!.Value, 8_501_100m, 8_501_200m);
    }

    [Fact]
    public void TvlFromBalances_MissingBalanceOrPrice_IsNull()
    {
        var one = new BigInteger(1_000_000);
        Assert.Null(PoolExplorerService.TvlFromBalances(null, 6, 1m, one, 6, 1m));
        Assert.Null(PoolExplorerService.TvlFromBalances(one, 6, 1m, null, 6, 1m));
        Assert.Null(PoolExplorerService.TvlFromBalances(one, 6, null, one, 6, 1m));
        Assert.Null(PoolExplorerService.TvlFromBalances(one, 6, 1m, one, 6, null));
    }

    [Fact]
    public void TvlFromBalances_EmptyPool_IsNull() =>
        Assert.Null(PoolExplorerService.TvlFromBalances(BigInteger.Zero, 18, 2000m, BigInteger.Zero, 6, 1m));

    [Fact]
    public void TvlFromBalances_AbsurdBalance_IsNullNotAnException() =>
        Assert.Null(PoolExplorerService.TvlFromBalances(BigInteger.Pow(10, 40), 0, 1m, BigInteger.One, 6, 1m));

    [Fact]
    public void Fees7d_IsSevenDayVolumeTimesFee()
    {
        var p = new PoolStats("id", Chain.Base, Protocol.UniswapV3, "A-B", "0xa", "0xb", 0.3m, null, 10_000_000m, 1_000_000m, 7_000_000m, null, null);

        Assert.Equal(21_000m, p.Fees7dUsd);
        Assert.Equal(7_000m, p.WithFee(0.1m).Fees7dUsd);
    }
}
