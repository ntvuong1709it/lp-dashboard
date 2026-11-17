using LpDashboard.Domain.Market;

namespace LpDashboard.Domain.Tests;

public class EventClassifierTests
{
    private static readonly MarketOptions O = new();

    private static Classified? Macro(string name, string country = "United States") =>
        EventClassifier.ClassifyMacro(country, name, O);

    [Fact]
    public void Core_cpi_is_high_inflation()
    {
        var c = Macro("Core CPI");
        Assert.Equal(EventImpact.High, c!.Impact);
        Assert.Equal(EventFamily.Inflation, c.Family);
    }

    [Fact]
    public void Rate_decision_is_high_with_its_own_family()
    {
        var c = Macro("Fed Interest Rate Decision");
        Assert.Equal(EventImpact.High, c!.Impact);
        Assert.Equal(EventFamily.RateDecision, c.Family);
    }

    [Theory]
    [InlineData("Retail Sales")]
    [InlineData("Core Retail Sales")]
    [InlineData("Initial Jobless Claims")]
    public void Medium_names(string name) => Assert.Equal(EventImpact.Medium, Macro(name)!.Impact);

    [Theory]
    [InlineData("Fed Chair Powell Speaks")]
    [InlineData("GDP Growth Rate QoQ")]
    [InlineData("core cpi")]
    [InlineData("FOMC Minutes")]
    public void High_names_are_whole_word_and_case_insensitive(string name) =>
        Assert.Equal(EventImpact.High, Macro(name)!.Impact);

    [Theory]
    [InlineData("FOMC Member Williams Speaks")]
    [InlineData("Atlanta Fed GDPNow")] // contains "GDP" but is a nowcast, seen in the real 2026-11-18 sample
    [InlineData("MBA Mortgage Applications")]
    [InlineData("Export Price Index")]
    [InlineData("Redbook")]
    public void Noise_is_dropped(string name) => Assert.Null(Macro(name));

    // Real names seen on Nasdaq's calendar on 2026-11-17 and for the 2026-12-10 FOMC day.
    [Theory]
    [InlineData("FOMC Press Conference", EventFamily.Other)]
    [InlineData("Core PCE Price Index", EventFamily.Inflation)]
    [InlineData("PCE price index", EventFamily.Inflation)]
    [InlineData("GDP", EventFamily.Other)]
    public void Real_release_names_are_kept(string name, EventFamily family)
    {
        var c = Macro(name);
        Assert.Equal(EventImpact.High, c!.Impact);
        Assert.Equal(family, c.Family);
    }

    [Theory]
    [InlineData("FOMC Statement")]          // the text of the decision, shown with the decision itself
    [InlineData("Dallas Fed PCE")]          // a regional survey
    [InlineData("Core PCE Prices")]         // GDP components, repeat the PCE release
    [InlineData("PCE Prices")]
    [InlineData("GDP Price Index")]
    [InlineData("GDP Sales")]
    [InlineData("Fed Vice Chair for Supervision Barr Speaks")]
    [InlineData("Fed's Balance Sheet")]
    public void Real_noise_names_are_dropped(string name) => Assert.Null(Macro(name));

    [Fact]
    public void Other_countries_are_dropped() => Assert.Null(Macro("Core CPI", "United Kingdom"));

    [Fact]
    public void A_word_inside_another_word_does_not_match() => Assert.Null(Macro("SCPIX Index"));

    [Theory]
    [InlineData("WMT", "$1,016,233,530,000", EventImpact.High)]
    [InlineData("XYZ", "$60,000,000,000", EventImpact.Medium)]
    [InlineData("NVDA", "N/A", EventImpact.High)]
    [InlineData("nvda", "$10", EventImpact.High)]
    public void Earnings_kept(string symbol, string cap, EventImpact expected) =>
        Assert.Equal(expected, EventClassifier.ClassifyEarnings(symbol, cap, O));

    [Theory]
    [InlineData("XYZ", "$5,000,000,000")]
    [InlineData("XYZ", null)]
    [InlineData("XYZ", "N/A")]
    public void Earnings_dropped(string symbol, string? cap) =>
        Assert.Null(EventClassifier.ClassifyEarnings(symbol, cap, O));
}
