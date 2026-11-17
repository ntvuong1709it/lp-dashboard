namespace LpDashboard.Domain.Market;

/// <summary>
/// Which events the Market watch shows and the thresholds of its rules (config section <c>Market</c>). Every default is
/// a starting point from docs/market-watch-spec.md section 5 and can be tuned without code changes. Name lists match
/// whole words, case-insensitively.
/// </summary>
public sealed class MarketOptions
{
    public const string SectionName = "Market";

    /// <summary>Only events of these countries (as Nasdaq spells them) are shown.</summary>
    public string[] Countries { get; set; } = ["United States"];

    public string[] HighImpactNames { get; set; } =
    [
        "Fed Interest Rate Decision", "FOMC Press Conference", "FOMC Minutes", "Fed Chair", "CPI", "Core CPI", "PCE", "Core PCE",
        "PPI", "Producer Price Index", "Nonfarm Payrolls", "Unemployment Rate", "GDP",
    ];

    public string[] MediumImpactNames { get; set; } =
        ["Retail Sales", "Initial Jobless Claims", "JOLTS", "ISM", "Consumer Confidence", "Michigan"];

    /// <summary>
    /// An event containing one of these is dropped even when a name above matches: "Atlanta Fed GDPNow" and "Dallas Fed PCE"
    /// are surveys, "FOMC Member ... Speaks" is not the chair, and "PCE Prices", "GDP Price Index" and "GDP Sales" are
    /// components of the GDP release that repeat the PCE and GDP rows already shown.
    /// </summary>
    public string[] ExcludeNames { get; set; } = ["GDPNow", "Member", "Dallas Fed", "PCE Prices", "GDP Price Index", "GDP Sales"];

    public string[] RateDecisionNames { get; set; } = ["Fed Interest Rate Decision"];

    public string[] InflationNames { get; set; } = ["CPI", "PCE", "PPI", "Producer Price Index"];

    public decimal EarningsHighMarketCapUsd { get; set; } = 200_000_000_000m;

    public decimal EarningsMediumMarketCapUsd { get; set; } = 50_000_000_000m;

    /// <summary>These symbols are High impact whatever their market cap.</summary>
    public string[] EarningsWatchlist { get; set; } = ["NVDA", "AAPL", "MSFT", "AMZN", "GOOGL", "META", "TSLA", "COIN", "MSTR"];

    /// <summary>An inflation print within this many percentage points of the forecast is Neutral.</summary>
    public decimal InflationTolerancePct { get; set; } = 0.05m;

    /// <summary>A price drop to liquidation above this is a Comfortable loan buffer.</summary>
    public decimal BufferThinBelowPercent { get; set; } = 40m;

    /// <summary>A price drop to liquidation below this is a Tight loan buffer.</summary>
    public decimal BufferTightBelowPercent { get; set; } = 25m;

    /// <summary>A stressed LTV within this many points of the margin-call (or liquidation) level is Watch.</summary>
    public decimal WatchWithinPoints { get; set; } = 8m;

    /// <summary>A stressed health factor below this is Watch.</summary>
    public decimal HfWatchBelow { get; set; } = 1.25m;

    /// <summary>The health factor the "add collateral" column restores.</summary>
    public decimal HfTarget { get; set; } = 1.2m;

    /// <summary>Price falls shown in the stress table, in percent.</summary>
    public int[] FallPercents { get; set; } = [10, 20, 30];
}
