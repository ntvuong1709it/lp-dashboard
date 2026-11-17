namespace LpDashboard.Domain.Market;

/// <summary>
/// Actual against forecast, as a fixed rule and never from free text. Only inflation and rate decisions are tagged: the
/// direction of jobs, growth and earnings depends on the market mood, so a tag there would sometimes be wrong. A missing
/// or unreadable number gives no tag.
/// </summary>
public static class ResultTagger
{
    private static readonly TagResult None = new(RiskTag.None, null);

    public static TagResult Tag(EventFamily family, string? actualRaw, string? consensusRaw, MarketOptions o)
    {
        if (family == EventFamily.Other) return None;
        if (!MarketText.TryParseValue(actualRaw, out var actual) || !MarketText.TryParseValue(consensusRaw, out var consensus)) return None;

        var a = MarketText.Clean(actualRaw);
        var c = MarketText.Clean(consensusRaw);
        var diff = actual - consensus;

        if (family == EventFamily.Inflation)
        {
            var lead = $"Actual {a} vs forecast {c}";
            if (Math.Abs(diff) <= o.InflationTolerancePct) return new(RiskTag.Neutral, $"{lead}: in line with the forecast.");
            return diff > 0
                ? new(RiskTag.RiskOff, $"{lead}: hotter than expected, fewer cuts priced in.")
                : new(RiskTag.RiskOn, $"{lead}: cooler than expected, more cuts priced in.");
        }

        var rate = $"Rate {a} vs forecast {c}";
        if (diff == 0) return new(RiskTag.Neutral, $"{rate}: as expected.");
        return diff > 0
            ? new(RiskTag.RiskOff, $"{rate}: higher than expected.")
            : new(RiskTag.RiskOn, $"{rate}: lower than expected.");
    }
}
