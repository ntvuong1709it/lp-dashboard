using LpDashboard.Domain.Market;

namespace LpDashboard.Domain.Tests;

public class MarketTextTests
{
    [Theory]
    [InlineData("&nbsp;", "")]
    [InlineData(" ", "")]
    [InlineData(null, "")]
    [InlineData("a &amp; b", "a & b")]
    [InlineData("  Core CPI ", "Core CPI")]
    public void Clean_decodes_and_trims(string? raw, string expected) =>
        Assert.Equal(expected, MarketText.Clean(raw));

    [Theory]
    [InlineData("3.1%", 3.1)]
    [InlineData("-0.4%", -0.4)]
    [InlineData("$0.64", 0.64)]
    [InlineData("1.380M", 1_380_000)]
    [InlineData("19.80K", 19_800)]
    [InlineData("$1,016,233,530,000", 1016233530000)]
    [InlineData("2.5B", 2_500_000_000)]
    public void TryParseValue_reads_units(string raw, double expected)
    {
        Assert.True(MarketText.TryParseValue(raw, out var value));
        Assert.Equal((decimal)expected, value);
    }

    [Theory]
    [InlineData("&nbsp;")]
    [InlineData(" ")]
    [InlineData("N/A")]
    [InlineData("abc")]
    [InlineData(null)]
    public void TryParseValue_rejects_blank_and_garbage(string? raw) =>
        Assert.False(MarketText.TryParseValue(raw, out _));
}
