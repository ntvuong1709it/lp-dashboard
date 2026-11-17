using System.Text.RegularExpressions;

namespace LpDashboard.Domain.Market;

/// <summary>Decides which calendar rows are shown and how important they are. Everything not matched is dropped.</summary>
public static class EventClassifier
{
    /// <returns>Null when the row is dropped: wrong country, an excluded name, or a name no list matches.</returns>
    public static Classified? ClassifyMacro(string country, string eventName, MarketOptions o)
    {
        var name = MarketText.Clean(eventName);
        if (!o.Countries.Any(c => string.Equals(c, MarketText.Clean(country), StringComparison.OrdinalIgnoreCase))) return null;
        if (MatchesAny(name, o.ExcludeNames)) return null;

        EventImpact impact;
        if (MatchesAny(name, o.HighImpactNames)) impact = EventImpact.High;
        else if (MatchesAny(name, o.MediumImpactNames)) impact = EventImpact.Medium;
        else return null;

        var family = MatchesAny(name, o.RateDecisionNames) ? EventFamily.RateDecision
            : MatchesAny(name, o.InflationNames) ? EventFamily.Inflation
            : EventFamily.Other;
        return new Classified(impact, family);
    }

    /// <returns>Null when the company is too small to show. A watchlist symbol is High even when its market cap is unreadable.</returns>
    public static EventImpact? ClassifyEarnings(string symbol, string? marketCapRaw, MarketOptions o)
    {
        if (o.EarningsWatchlist.Any(s => string.Equals(s, symbol.Trim(), StringComparison.OrdinalIgnoreCase))) return EventImpact.High;
        if (!MarketText.TryParseValue(marketCapRaw, out var cap)) return null;
        if (cap >= o.EarningsHighMarketCapUsd) return EventImpact.High;
        if (cap >= o.EarningsMediumMarketCapUsd) return EventImpact.Medium;
        return null;
    }

    private static bool MatchesAny(string text, IEnumerable<string> names) =>
        names.Any(n => Regex.IsMatch(text, $"(?<![A-Za-z0-9]){Regex.Escape(n)}(?![A-Za-z0-9])", RegexOptions.IgnoreCase));
}
