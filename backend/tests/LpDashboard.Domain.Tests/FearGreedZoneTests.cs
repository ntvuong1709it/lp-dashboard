using LpDashboard.Domain.Market;

namespace LpDashboard.Domain.Tests;

public class FearGreedZoneTests
{
    [Theory]
    [InlineData(0, 25, FearGreedZone.ExtremeFear)]
    [InlineData(26, 46, FearGreedZone.Fear)]
    [InlineData(47, 54, FearGreedZone.Neutral)]
    [InlineData(55, 75, FearGreedZone.Greed)]
    [InlineData(76, 100, FearGreedZone.ExtremeGreed)]
    public void Value_maps_to_its_zone(int first, int last, FearGreedZone zone)
    {
        Assert.Equal(zone, FearGreedZones.Of(first));
        Assert.Equal(zone, FearGreedZones.Of(last));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Values_outside_0_to_100_throw(int value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => FearGreedZones.Of(value));

    [Fact]
    public void Bands_cover_0_to_100_without_gap_or_overlap()
    {
        var bands = FearGreedZones.Bands;

        Assert.Equal(0, bands[0].From);
        Assert.Equal(100, bands[^1].To);
        for (var i = 1; i < bands.Count; i++) Assert.Equal(bands[i - 1].To + 1, bands[i].From);
        foreach (var b in bands)
        {
            Assert.Equal(b.Zone, FearGreedZones.Of(b.From));
            Assert.Equal(b.Zone, FearGreedZones.Of(b.To));
        }
    }
}
