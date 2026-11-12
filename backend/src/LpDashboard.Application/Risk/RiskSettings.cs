namespace LpDashboard.Application.Risk;

/// <summary>Operational settings of the risk screen (config section <c>Risk</c>). Thresholds live in <see cref="LpDashboard.Domain.Risk.RiskOptions"/>.</summary>
public sealed class RiskSettings
{
    public const string SectionName = "Risk";

    /// <summary>A check younger than this is served from the database without calling any source.</summary>
    public int CacheHours { get; set; } = 24;

    /// <summary>A forced re-check of one token is refused for this long after the last check.</summary>
    public int RefreshCooldownMinutes { get; set; } = 5;

    /// <summary>Hour (UTC) of the nightly re-check of held and starred tokens.</summary>
    public int NightlyUtcHour { get; set; } = 3;

    /// <summary>How often pool token balances are snapshotted for held and starred pools.</summary>
    public int LiquidityIntervalMinutes { get; set; } = 60;

    /// <summary>Pool value (current prices) down more than this against the snapshot about a day earlier alerts.</summary>
    public decimal LiquidityDropPct { get; set; } = 30m;

    public int LiquidityRetentionDays { get; set; } = 30;

    /// <summary>Checks kept per subject, for change detection and the recent-changes list.</summary>
    public int HistoryKeep { get; set; } = 30;

    public int MaxConcurrentChecks { get; set; } = 2;

    /// <summary>Per source. Includes time spent waiting for the source's rate limiter.</summary>
    public int SourceTimeoutSeconds { get; set; } = 25;

    /// <summary>How far back the price-history check looks.</summary>
    public int PriceHistoryDays { get; set; } = 30;

    /// <summary>Spreads the nightly job: pause between two token checks.</summary>
    public int NightlyPauseSeconds { get; set; } = 3;

    public EtherscanSettings Etherscan { get; set; } = new();
    public SourceRateSettings GoPlus { get; set; } = new();
    public SourceRateSettings HoneypotIs { get; set; } = new();
    public SourceRateSettings DexScreener { get; set; } = new() { RequestsPerMinute = 60 };
}

public sealed class EtherscanSettings
{
    /// <summary>Empty disables the source. Keep the key in user-secrets or the environment, never in appsettings.json.</summary>
    public string? ApiKey { get; set; }
}

public sealed class SourceRateSettings
{
    /// <summary>Token bucket: at most this many requests per minute to the source.</summary>
    public int RequestsPerMinute { get; set; } = 20;
}
