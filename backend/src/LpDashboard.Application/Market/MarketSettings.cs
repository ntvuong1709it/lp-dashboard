namespace LpDashboard.Application.Market;

/// <summary>Operational settings of the Market watch (config section <c>Market</c>). The event rules live in <see cref="LpDashboard.Domain.Market.MarketOptions"/>.</summary>
public sealed class MarketSettings
{
    public const string SectionName = "Market";

    /// <summary>Per Nasdaq source. Includes time spent waiting for the limiter.</summary>
    public int RequestsPerMinute { get; set; } = 240;

    /// <summary>
    /// How many calendar days are fetched at once. Measured on 2026-11-17: a cold 14-day load takes about 12 s at 4 and
    /// 9 s at 8 (Nasdaq answers in 1 to 3 s per call), then 0.1 s from the cache.
    /// </summary>
    public int MaxConcurrentDays { get; set; } = 8;

    public int SourceTimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// Calendar of a day after today. A refresh of the whole window blocks the page for several seconds, so this is long:
    /// the schedule hardly changes, only forecasts drift.
    /// </summary>
    public int UpcomingTtlMinutes { get; set; } = 180;

    /// <summary>Calendar of today, and of a past day that has no published result yet.</summary>
    public int TodayTtlMinutes { get; set; } = 10;

    /// <summary>Calendar of a past day once a result is published.</summary>
    public int PastTtlHours { get; set; } = 24;

    public int HeadlinesTtlMinutes { get; set; } = 15;

    /// <summary>The Fear and Greed index changes once a day, so an hour is plenty.</summary>
    public int FearGreedTtlMinutes { get; set; } = 60;

    /// <summary>When a refresh fails, data up to this old is still shown and marked stale.</summary>
    public int StaleMaxHours { get; set; } = 24;

    /// <summary>RSS feeds shown as headlines. A URL listed twice is read once.</summary>
    public List<HeadlineFeed> HeadlineFeeds { get; set; } =
        [new HeadlineFeed { Name = "Federal Reserve", Url = "https://www.federalreserve.gov/feeds/press_monetary.xml" }];
}

public sealed class HeadlineFeed
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
}
