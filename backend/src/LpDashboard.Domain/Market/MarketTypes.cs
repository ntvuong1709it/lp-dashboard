namespace LpDashboard.Domain.Market;

public enum EventCategory { Macro, Earnings }

public enum EventImpact { Medium, High }

/// <summary>Which tagging rule applies. Anything that is neither inflation nor a rate decision is never tagged.</summary>
public enum EventFamily { Other, Inflation, RateDecision }

public enum RiskTag { None, RiskOn, RiskOff, Neutral }

public sealed record Classified(EventImpact Impact, EventFamily Family);

/// <summary>A tag with the numbers behind it. <see cref="Reason"/> is null exactly when the tag is <see cref="RiskTag.None"/>.</summary>
public sealed record TagResult(RiskTag Tag, string? Reason);
