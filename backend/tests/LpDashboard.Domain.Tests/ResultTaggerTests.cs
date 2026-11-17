using LpDashboard.Domain.Market;

namespace LpDashboard.Domain.Tests;

public class ResultTaggerTests
{
    private static readonly MarketOptions O = new();

    [Fact]
    public void Hotter_inflation_is_risk_off_with_the_numbers()
    {
        var r = ResultTagger.Tag(EventFamily.Inflation, "3.1%", "3.0%", O);
        Assert.Equal(RiskTag.RiskOff, r.Tag);
        Assert.Contains("3.1%", r.Reason);
        Assert.Contains("3.0%", r.Reason);
        Assert.Contains("hotter", r.Reason);
    }

    [Fact]
    public void Cooler_inflation_is_risk_on()
    {
        var r = ResultTagger.Tag(EventFamily.Inflation, "2.9%", "3.0%", O);
        Assert.Equal(RiskTag.RiskOn, r.Tag);
        Assert.Contains("cooler", r.Reason);
    }

    [Fact]
    public void Inflation_within_tolerance_is_neutral() =>
        Assert.Equal(RiskTag.Neutral, ResultTagger.Tag(EventFamily.Inflation, "3.04%", "3.0%", O).Tag);

    [Theory]
    [InlineData("3.1%", "")]
    [InlineData("&nbsp;", "3.0%")]
    [InlineData("abc", "3.0%")]
    [InlineData(null, "3.0%")]
    public void Missing_or_unreadable_values_give_no_tag_and_no_reason(string? actual, string? consensus)
    {
        var r = ResultTagger.Tag(EventFamily.Inflation, actual, consensus, O);
        Assert.Equal(RiskTag.None, r.Tag);
        Assert.Null(r.Reason);
    }

    [Theory]
    [InlineData("4.25%", "4.00%", RiskTag.RiskOff)]
    [InlineData("4.00%", "4.00%", RiskTag.Neutral)]
    [InlineData("3.75%", "4.00%", RiskTag.RiskOn)]
    public void Rate_decision_against_forecast(string actual, string consensus, RiskTag expected) =>
        Assert.Equal(expected, ResultTagger.Tag(EventFamily.RateDecision, actual, consensus, O).Tag);

    [Fact]
    public void Rate_decision_has_no_tolerance() =>
        Assert.Equal(RiskTag.RiskOff, ResultTagger.Tag(EventFamily.RateDecision, "4.01%", "4.00%", O).Tag);

    [Fact]
    public void Other_events_are_never_tagged() =>
        Assert.Equal(RiskTag.None, ResultTagger.Tag(EventFamily.Other, "250K", "180K", O).Tag);
}
