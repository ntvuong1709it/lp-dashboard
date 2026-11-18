using LpDashboard.Domain.Market;

namespace LpDashboard.Domain.Tests;

public class PremiumZoneTests
{
    public static TheoryData<decimal, PremiumZone> Cases => new()
    {
        { -0.30m, PremiumZone.SellingHard },
        { -0.10m, PremiumZone.SellingHard },
        { -0.0999m, PremiumZone.SellingABit },
        { -0.04m, PremiumZone.SellingABit },
        { -0.03m, PremiumZone.Flat },
        { 0m, PremiumZone.Flat },
        { 0.03m, PremiumZone.Flat },
        { 0.04m, PremiumZone.BuyingABit },
        { 0.0999m, PremiumZone.BuyingABit },
        { 0.10m, PremiumZone.BuyingHard },
        { 0.55m, PremiumZone.BuyingHard },
        { -0.55m, PremiumZone.SellingHard },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Percent_maps_to_its_zone(decimal percent, PremiumZone zone) =>
        Assert.Equal(zone, PremiumZones.Of(percent));

    [Fact]
    public void Bands_are_contiguous_from_minus_to_plus_the_gauge_span()
    {
        var bands = PremiumZones.Bands;

        Assert.Equal(-PremiumZones.GaugeSpan, bands[0].From);
        Assert.Equal(0.30m, bands[^1].To);
        for (var i = 1; i < bands.Count; i++) Assert.Equal(bands[i - 1].To, bands[i].From);
    }

    [Theory]
    [InlineData(101, 100, 1.0, 1.0)]
    [InlineData(99, 100, 1.0, -1.0)]
    [InlineData(100, 100, 1.0, 0.0)]
    public void Hourly_gap_is_the_dollar_corrected_difference(double coinbase, double binance, double usdt, double expected) =>
        Assert.Equal((decimal)expected, PremiumMath.HourlyPercent((decimal)coinbase, (decimal)binance, (decimal)usdt));

    [Fact]
    public void Hourly_gap_on_the_real_2026_11_18_prices_is_about_zero_not_minus_0_11()
    {
        // Coinbase BTC-USD 63472.77, Binance BTC-USDT 63543.97, Coinbase USDT-USD 0.99888. Raw against USDT it is -0.11.
        var gap = PremiumMath.HourlyPercent(63472.77m, 63543.97m, 0.99888m);

        Assert.InRange(gap, -0.001m, 0.001m);
    }

    [Theory]
    [InlineData(0, 100, 1)]
    [InlineData(-1, 100, 1)]
    [InlineData(100, 0, 1)]
    [InlineData(100, -1, 1)]
    [InlineData(100, 100, 0)]
    [InlineData(100, 100, -1)]
    public void Non_positive_prices_throw(double coinbase, double binance, double usdt) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PremiumMath.HourlyPercent((decimal)coinbase, (decimal)binance, (decimal)usdt));

    [Theory]
    [InlineData(0.035, 0.04)]
    [InlineData(-0.035, -0.04)]
    [InlineData(0.0349, 0.03)]
    public void Round_goes_to_two_decimals_away_from_zero(double value, double expected) =>
        Assert.Equal((decimal)expected, PremiumMath.Round((decimal)value));
}
