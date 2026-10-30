using LpDashboard.Application.Abstractions;

namespace LpDashboard.Domain.Tests;

public class StakePeriodTests
{
    private const string Gauge = "0x00000000000000000000000000000000000000aa";

    // Mirrors position #70220435: staked after mint, unstaked on withdraw; claims from an earlier position must not match.
    private static readonly DiscoveredPosition Position = new(
        "npm", "70220435", 45857089, IsStaked: false, Gauge, IsBurned: true,
        [new StakePeriod(Gauge, 45857100, 5, 46765492, 20)]);

    [Theory]
    [InlineData(45465832, 0)]
    [InlineData(45856974, 300)]
    [InlineData(45857100, 5)]
    [InlineData(46765492, 20)]
    [InlineData(46765495, 0)]
    public void ClaimOutsideStakePeriod_IsNotAttributed(long block, int logIndex) =>
        Assert.False(Position.WasStakedIn(Gauge, block, logIndex));

    [Theory]
    [InlineData(45857100, 6)]
    [InlineData(46071252, 0)]
    [InlineData(46765492, 19)]
    public void ClaimInsideStakePeriod_IsAttributed(long block, int logIndex) =>
        Assert.True(Position.WasStakedIn(Gauge.ToUpperInvariant(), block, logIndex));

    [Fact]
    public void OpenPeriod_CoversEverythingAfterStake() =>
        Assert.True(new StakePeriod(Gauge, 100, 1).Covers(long.MaxValue, 0));

    [Fact]
    public void ClaimFromOtherGauge_IsNotAttributed() =>
        Assert.False(Position.WasStakedIn("0x00000000000000000000000000000000000000bb", 46071252, 0));
}
